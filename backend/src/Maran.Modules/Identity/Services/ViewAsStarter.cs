using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Interfaces;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Utilities.Tokens;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// The one place a view-as session is started and its start is journalled, for both kinds of subject
/// (issues #317, #179): the two start handlers decide WHO may be viewed and call this for the rest.
/// </summary>
/// <remarks>
/// <para>
/// <b>One hop.</b> This builds a session and a token for a single viewed login. It is reachable only
/// from the two start handlers, whose endpoints refuse a view-as token, so a token it signs can never
/// be the means of starting another. The token carries the VIEWED login's role, which is how an
/// administrator viewing as a reseller keeps no administrator power.
/// </para>
/// <para>
/// <b>The operator is re-read from the database on every start.</b> A token outlives the facts it
/// was issued on: an operator since demoted, suspended or locked out must not start a view-as with
/// the old one, and a reseller operator whose reseller has been suspended must not either. The reseller
/// is read through <see cref="IResellerDirectory"/> because this module may not read the accounts
/// schema.
/// </para>
/// </remarks>
public sealed class ViewAsStarter
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>The window onto the resellers the accounts module owns, read for a reseller operator's standing.</summary>
    private readonly IResellerDirectory _resellerDirectory;

    /// <summary>Signs the view-as access token.</summary>
    private readonly IAccessTokenIssuer _accessTokenIssuer;

    /// <summary>Records the start and its refusals.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the starter.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="resellerDirectory">The window onto the resellers the accounts module owns.</param>
    /// <param name="accessTokenIssuer">Signs the view-as access token.</param>
    /// <param name="journal">Records the start and its refusals.</param>
    /// <param name="clock">The panel's clock.</param>
    public ViewAsStarter(
        IdentityDbContext dbContext,
        IResellerDirectory resellerDirectory,
        IAccessTokenIssuer accessTokenIssuer,
        IdentityAuditJournal journal,
        IClock clock)
    {
        _dbContext = dbContext;
        _resellerDirectory = resellerDirectory;
        _accessTokenIssuer = accessTokenIssuer;
        _journal = journal;
        _clock = clock;
    }

    /// <summary>Re-reads the operator and reports whether they may start a view-as at all.</summary>
    /// <param name="operatorUserId">The operator, from their own access token.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// The operator's row when it is an administrator or a reseller that may sign in, and, for a
    /// reseller, whose reseller is active; otherwise null. Which of the two the row is stays for the
    /// handler to ask, because the edges differ by role.
    /// </returns>
    public async Task<User?> FindOperatorAsync(Guid operatorUserId, CancellationToken cancellationToken)
    {
        var actor = await _dbContext.Users
            .SingleOrDefaultAsync(u => u.Id == operatorUserId, cancellationToken);
        if (actor is null || !actor.MayAuthenticate)
        {
            return null;
        }

        if (actor.Role == UserRole.Admin)
        {
            return actor;
        }

        if (actor.Role == UserRole.Reseller && actor.ResellerId is { } resellerId)
        {
            var reseller = await _resellerDirectory.FindAsync(resellerId, cancellationToken);
            return reseller?.State == ResellerState.Active ? actor : null;
        }

        return null;
    }

    /// <summary>Starts the session, signs its token and journals the start.</summary>
    /// <param name="actor">The operator viewing, as <see cref="FindOperatorAsync"/> returned it.</param>
    /// <param name="viewed">The login being viewed as: the account's owner or the reseller's login.</param>
    /// <param name="subject">What is being viewed; its kind must agree with <paramref name="viewed"/>'s role.</param>
    /// <param name="operatorSessionId">The operator's own session, from their access token.</param>
    /// <param name="ipAddress">The operator's address.</param>
    /// <param name="userAgent">The operator's user agent.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The view-as token and what the banner shows.</returns>
    public async Task<ViewAsSessionDto> StartAsync(
        User actor,
        User viewed,
        ViewAsSubject subject,
        Guid operatorSessionId,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken)
    {
        // A reseller view has no account to speak for; an account view speaks for the one chosen.
        Guid? accountId = subject.Kind == ViewAsKind.Account ? subject.Id : null;

        // The refresh token is generated only because the column demands a unique hash; its
        // plaintext is dropped here and never reaches a response or a cookie.
        var session = Session.StartViewAs(
            Guid.NewGuid(),
            viewed.Id,
            accountId,
            actor.Id,
            operatorSessionId,
            RefreshTokenHasher.Hash(RefreshTokenHasher.Generate()),
            _clock.UtcNow,
            ipAddress,
            userAgent);
        _dbContext.Sessions.Add(session);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var token = _accessTokenIssuer.IssueViewAs(viewed, session, actor);

        await RecordAsync(
            actor.Id,
            actor.Username,
            ViewAsSubjectText.Of(subject.Kind, subject.Name, viewed.Username),
            succeeded: true,
            ipAddress,
            userAgent,
            cancellationToken);

        var user = new AuthenticatedUserDto(viewed.Id, viewed.Username, viewed.Email, viewed.Role, accountId);
        var viewing = new ViewingAsDto(
            subject.Kind,
            subject.Id,
            subject.Name,
            viewed.Username,
            actor.Username,
            session.ExpiresAt);

        return new ViewAsSessionDto(token.Value, token.ExpiresAt, user, viewing);
    }

    /// <summary>Journals a refused start with the operator as actor.</summary>
    /// <param name="operatorUserId">The operator named by their own token.</param>
    /// <param name="actor">The operator's row, when it was found.</param>
    /// <param name="subject">The viewed identity, or the id asked for when it could not be resolved.</param>
    /// <param name="ipAddress">The operator's address.</param>
    /// <param name="userAgent">The operator's user agent.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <returns>Resolves once the entry is stored.</returns>
    public async Task RecordRefusalAsync(
        Guid operatorUserId,
        User? actor,
        string subject,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken)
    {
        await RecordAsync(
            operatorUserId,
            actor?.Username ?? string.Empty,
            subject,
            succeeded: false,
            ipAddress,
            userAgent,
            cancellationToken);
    }

    /// <summary>Journals a start or a refused start.</summary>
    /// <param name="operatorUserId">The operator.</param>
    /// <param name="operatorUsername">The operator's name, empty when the row was not found.</param>
    /// <param name="subject">What the start was attempted on.</param>
    /// <param name="succeeded">Whether the session was started.</param>
    /// <param name="ipAddress">The operator's address.</param>
    /// <param name="userAgent">The operator's user agent.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <returns>Resolves once the entry is stored.</returns>
    private async Task RecordAsync(
        Guid operatorUserId,
        string operatorUsername,
        string subject,
        bool succeeded,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken)
    {
        await _journal.RecordViewAsAsync(
            operatorUserId,
            operatorUsername,
            IdentityAuditActions.ViewAsStarted,
            subject,
            ipAddress,
            userAgent,
            succeeded,
            cancellationToken);
    }
}
