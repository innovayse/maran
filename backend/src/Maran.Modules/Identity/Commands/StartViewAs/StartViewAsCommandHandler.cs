using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Identity.Commands.StartViewAs;

/// <summary>
/// Handles <see cref="StartViewAsCommand"/>: issues a short-lived view-as session for the login that
/// owns an account, and journals the start with the operator as actor (issues #317, #179).
/// </summary>
/// <remarks>
/// <para>
/// <b>Who may view as whom — the scope rule.</b> An administrator may view as any account's owning
/// login; a reseller may view as the owning login of one of ITS OWN accounts and of no other. Both
/// are one line: the target account is resolved through <see cref="IAccountDirectory.FindAsync"/>,
/// which is scoped to the CALLER and answers null for an account outside that scope, and a null is a
/// 404 indistinguishable from "no such account". A reseller naming another reseller's account, or an
/// account that does not exist, therefore gets the same answer. This handler builds no second scoping
/// mechanism; the endpoint's policy admits both roles and the directory narrows.
/// </para>
/// <para>
/// <b>Both logins are re-read, and both are asked <see cref="User.MayAuthenticate"/>.</b>
/// The operator's role is read from the database, not the token (<see cref="ViewAsStarter"/>), so a
/// token issued before a demotion or a suspension cannot start a view-as session; and a viewed login
/// that may not sign in itself, or whose membership of the viewed account is suspended, may not be
/// viewed as either, because the token minted here is a sign-in as that login for that account.
/// </para>
/// </remarks>
public sealed class StartViewAsCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>The tenant-scoped window onto the accounts module.</summary>
    private readonly IAccountDirectory _accountDirectory;

    /// <summary>Starts the session and records the start and its refusals.</summary>
    private readonly ViewAsStarter _starter;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="accountDirectory">The tenant-scoped window onto the accounts module.</param>
    /// <param name="starter">Starts the session and records the start and its refusals.</param>
    public StartViewAsCommandHandler(
        IdentityDbContext dbContext,
        IAccountDirectory accountDirectory,
        ViewAsStarter starter)
    {
        _dbContext = dbContext;
        _accountDirectory = accountDirectory;
        _starter = starter;
    }

    /// <summary>Starts the view-as session.</summary>
    /// <param name="command">The account, and the operator stamped from their own token.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// The view-as token and what the banner shows; <c>ViewAsAccountNotFound</c> (404) for an account
    /// that does not exist, lies outside the caller's scope, or has no owning login, or when the
    /// operator is no longer an administrator or an active reseller who may sign in;
    /// <c>ViewAsLoginUnavailable</c> (409) when the owning login may not sign in.
    /// </returns>
    public async Task<Result<ViewAsSessionDto>> HandleAsync(StartViewAsCommand command, CancellationToken cancellationToken)
    {
        var refusedSubject = command.AccountId.ToString();

        var actor = await _starter.FindOperatorAsync(command.OperatorUserId, cancellationToken);
        if (actor is null)
        {
            // The policy already required an administrator or reseller token; this is the same
            // question asked of the row, for a token that outlived the facts it was issued on.
            await RefuseAsync(command, actor, refusedSubject, cancellationToken);
            return Result<ViewAsSessionDto>.Fail(Error.Of(nameof(ErrorMessages.ViewAsAccountNotFound), ErrorType.NotFound));
        }

        // Scope rule: the directory answers null for an account the caller may not see (see remarks).
        var account = await _accountDirectory.FindAsync(command.AccountId, cancellationToken);
        var owner = account is null ? null : await FindOwnerAsync(account.Id, cancellationToken);
        if (account is null || owner is null)
        {
            await RefuseAsync(command, actor, refusedSubject, cancellationToken);
            return Result<ViewAsSessionDto>.Fail(Error.Of(nameof(ErrorMessages.ViewAsAccountNotFound), ErrorType.NotFound));
        }

        var viewed = owner.Login;
        var subject = new ViewAsSubject(ViewAsKind.Account, account.Id, account.Username);
        if (!viewed.MayAuthenticate || owner.Membership.State == MembershipState.Suspended)
        {
            await RefuseAsync(command, actor, ViewAsSubjectText.Of(subject.Kind, subject.Name, viewed.Username), cancellationToken);
            return Result<ViewAsSessionDto>.Fail(Error.Of(nameof(ErrorMessages.ViewAsLoginUnavailable), ErrorType.Conflict));
        }

        var started = await _starter.StartAsync(
            actor,
            viewed,
            subject,
            command.OperatorSessionId,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<ViewAsSessionDto>.Ok(started);
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

    /// <summary>Journals a refused start with the operator as actor.</summary>
    /// <param name="command">The command, for the operator and the origin.</param>
    /// <param name="actor">The operator's row, when it was found.</param>
    /// <param name="subject">The viewed identity, or the account id asked for.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <returns>Resolves once the entry is stored.</returns>
    private async Task RefuseAsync(
        StartViewAsCommand command,
        User? actor,
        string subject,
        CancellationToken cancellationToken)
    {
        await _starter.RecordRefusalAsync(
            command.OperatorUserId,
            actor,
            subject,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);
    }
}
