using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Interfaces;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Utilities.Tokens;

namespace Maran.Modules.Identity.Commands.StartViewAs;

/// <summary>
/// Handles <see cref="StartViewAsCommand"/>: issues a short-lived view-as session for the login that
/// owns an account, and journals the start with the operator as actor (issue #317).
/// </summary>
/// <remarks>
/// <para>
/// <b>Who may view as whom — the scope rule.</b> An administrator may view as any account's owning
/// login. The target account is resolved through <see cref="IAccountDirectory.FindAsync"/>, which is
/// scoped to the CALLER and answers null for an account outside that scope, and a null is a 404
/// indistinguishable from "no such account". That is the line a reseller will be held to: when #179
/// gives the directory a reseller scope, a reseller may view as only its own accounts, and naming
/// another reseller's account answers 404 from this same line. This handler builds no reseller model;
/// the endpoint stays administrator-only until #179 widens its policy.
/// </para>
/// <para>
/// <b>Both logins are re-read, and both are asked <see cref="User.MayAuthenticate"/>.</b> The
/// operator's role is read from the database, not the token, so a token issued before a demotion or a
/// suspension cannot start a view-as session; and a viewed login that may not sign in itself, or
/// whose membership of the viewed account is suspended, may not be viewed as either, because the
/// token minted here is a sign-in as that login for that account.
/// </para>
/// </remarks>
public sealed class StartViewAsCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>The tenant-scoped window onto the accounts module.</summary>
    private readonly IAccountDirectory _accountDirectory;

    /// <summary>Signs the view-as access token.</summary>
    private readonly IAccessTokenIssuer _accessTokenIssuer;

    /// <summary>Records the start and its refusals.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="accountDirectory">The tenant-scoped window onto the accounts module.</param>
    /// <param name="accessTokenIssuer">Signs the view-as access token.</param>
    /// <param name="journal">Records the start and its refusals.</param>
    /// <param name="clock">The panel's clock.</param>
    public StartViewAsCommandHandler(
        IdentityDbContext dbContext,
        IAccountDirectory accountDirectory,
        IAccessTokenIssuer accessTokenIssuer,
        IdentityAuditJournal journal,
        IClock clock)
    {
        _dbContext = dbContext;
        _accountDirectory = accountDirectory;
        _accessTokenIssuer = accessTokenIssuer;
        _journal = journal;
        _clock = clock;
    }

    /// <summary>Starts the view-as session.</summary>
    /// <param name="command">The account, and the operator stamped from their own token.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// The view-as token and what the banner shows; <c>ViewAsAccountNotFound</c> (404) for an account
    /// that does not exist, lies outside the caller's scope, or has no owning login;
    /// <c>ViewAsLoginUnavailable</c> (409) when that login may not sign in; or the panel's ordinary
    /// 403 code when the operator is no longer an administrator who may sign in.
    /// </returns>
    public async Task<Result<ViewAsSessionDto>> HandleAsync(StartViewAsCommand command, CancellationToken cancellationToken)
    {
        var refusedSubject = command.AccountId.ToString();

        var actor = await _dbContext.Users
            .SingleOrDefaultAsync(u => u.Id == command.OperatorUserId, cancellationToken);
        if (actor is null || actor.Role != UserRole.Admin || !actor.MayAuthenticate)
        {
            // The policy already required an administrator token; this is the same question asked of
            // the row, for a token that outlived the facts it was issued on.
            await RecordAsync(command, actor, refusedSubject, succeeded: false, cancellationToken);
            return Result<ViewAsSessionDto>.Fail(Error.Of(nameof(ErrorMessages.ViewAsAccountNotFound), ErrorType.NotFound));
        }

        // Scope rule: the directory answers null for an account the caller may not see (see remarks).
        var account = await _accountDirectory.FindAsync(command.AccountId, cancellationToken);
        var owner = account is null ? null : await FindOwnerAsync(account.Id, cancellationToken);
        if (account is null || owner is null)
        {
            await RecordAsync(command, actor, refusedSubject, succeeded: false, cancellationToken);
            return Result<ViewAsSessionDto>.Fail(Error.Of(nameof(ErrorMessages.ViewAsAccountNotFound), ErrorType.NotFound));
        }

        var viewed = owner.Login;
        var subject = $"account {account.Username} as {viewed.Username}";
        if (!viewed.MayAuthenticate || owner.Membership.State == MembershipState.Suspended)
        {
            await RecordAsync(command, actor, subject, succeeded: false, cancellationToken);
            return Result<ViewAsSessionDto>.Fail(Error.Of(nameof(ErrorMessages.ViewAsLoginUnavailable), ErrorType.Conflict));
        }

        // The refresh token is generated only because the column demands a unique hash; its
        // plaintext is dropped here and never reaches a response or a cookie.
        var session = Session.StartViewAs(
            Guid.NewGuid(),
            viewed.Id,
            account.Id,
            actor.Id,
            command.OperatorSessionId,
            RefreshTokenHasher.Hash(RefreshTokenHasher.Generate()),
            _clock.UtcNow,
            command.IpAddress,
            command.UserAgent);
        _dbContext.Sessions.Add(session);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var token = _accessTokenIssuer.IssueViewAs(viewed, session, actor);

        await RecordAsync(command, actor, subject, succeeded: true, cancellationToken);

        var user = new AuthenticatedUserDto(viewed.Id, viewed.Username, viewed.Email, viewed.Role, account.Id);
        var viewing = new ViewingAsDto(account.Id, account.Username, viewed.Username, actor.Username, session.ExpiresAt);

        return Result<ViewAsSessionDto>.Ok(new ViewAsSessionDto(token.Value, token.ExpiresAt, user, viewing));
    }

    /// <summary>Finds the customer login that owns an account, with its membership.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The owning login and its owner membership, or null when the account has none.</returns>
    private async Task<AccountOwner?> FindOwnerAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var found = await (
            from membership in _dbContext.AccountMemberships.AsNoTracking()
            where membership.AccountId == accountId && membership.Role == MembershipRole.Owner
            join login in _dbContext.Users.AsNoTracking() on membership.UserId equals login.Id
            where login.Role == UserRole.Customer
            orderby membership.CreatedAt
            select new { login, membership }).FirstOrDefaultAsync(cancellationToken);

        return found is null ? null : new AccountOwner(found.login, found.membership);
    }

    /// <summary>Journals a start or a refused start with the operator as actor.</summary>
    /// <param name="command">The command, for the operator and the origin.</param>
    /// <param name="actor">The operator's row, when it was found.</param>
    /// <param name="subject">The viewed identity, or the account id asked for.</param>
    /// <param name="succeeded">Whether the session was started.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <returns>Resolves once the entry is stored.</returns>
    private async Task RecordAsync(
        StartViewAsCommand command,
        User? actor,
        string subject,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        await _journal.RecordViewAsAsync(
            command.OperatorUserId,
            actor?.Username ?? string.Empty,
            AuditActions.ViewAsStarted,
            subject,
            command.IpAddress,
            command.UserAgent,
            succeeded,
            cancellationToken);
    }
}
