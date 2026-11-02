using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Interfaces;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Commands.SwitchActiveAccount;

/// <summary>
/// Handles <see cref="SwitchActiveAccountCommand"/> by moving the caller's session to another
/// account they hold a membership of, and re-signing the access token for it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The membership row is the only authority.</b> The account id arrives in the request body, so
/// the handler looks for a membership of exactly (this caller, that account) and acts on nothing
/// else. An account the caller has no membership of answers <c>AccountMembershipNotFound</c> (404),
/// the same answer as an account that does not exist, so the endpoint cannot be used to find out
/// which account ids are real (rules/security.md item 6).
/// </para>
/// <para>
/// <b>Why a new token and not a changed one.</b> The account every tenant query follows is a claim
/// inside the signed access token. A signed token cannot be amended, so the switch stores the choice
/// on the session (where the next refresh will read it) and issues a replacement token in the same
/// response; the old token keeps its old account until it expires, which is at most fifteen minutes
/// and is the limit of any claim-based scheme.
/// </para>
/// <para>
/// <b>Refused attempts are journalled too.</b> A switch aimed at an account the caller holds no
/// membership of is the one outcome here worth an operator's attention, so it is recorded with
/// <c>succeeded: false</c> and the requested id as its subject.
/// </para>
/// </remarks>
public sealed class SwitchActiveAccountCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Signs the replacement access token.</summary>
    private readonly IAccessTokenIssuer _accessTokenIssuer;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Records the switch and the refused attempts.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="accessTokenIssuer">Signs the replacement access token.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="journal">Records the switch and the refused attempts.</param>
    public SwitchActiveAccountCommandHandler(
        IdentityDbContext dbContext,
        IAccessTokenIssuer accessTokenIssuer,
        IClock clock,
        IdentityAuditJournal journal)
    {
        _dbContext = dbContext;
        _accessTokenIssuer = accessTokenIssuer;
        _clock = clock;
        _journal = journal;
    }

    /// <summary>Switches the caller's session to the requested account and issues a token for it.</summary>
    /// <param name="command">The requested account, and who is asking from which session.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// The replacement access token and the account it speaks for; <c>AccountMembershipNotFound</c>
    /// when the caller holds no membership of the account; <c>SessionNotFound</c> when the session in
    /// the caller's token is not one of theirs or is no longer live; or the sign-in refusal when the
    /// login may no longer be used.
    /// </returns>
    public async Task<Result<ActiveAccountDto>> HandleAsync(
        SwitchActiveAccountCommand command,
        CancellationToken cancellationToken)
    {
        var isMember = await _dbContext.AccountMemberships
            .AsNoTracking()
            .AnyAsync(
                membership => membership.UserId == command.UserId && membership.AccountId == command.AccountId,
                cancellationToken);

        if (!isMember)
        {
            await _journal.RecordIdentifiedAsync(
                command.UserId,
                AuditActions.ActiveAccountSwitched,
                command.AccountId.ToString(),
                command.IpAddress,
                command.UserAgent,
                succeeded: false,
                cancellationToken);

            return Result<ActiveAccountDto>.Fail(
                Error.Of(nameof(ErrorMessages.AccountMembershipNotFound), ErrorType.NotFound));
        }

        var session = await _dbContext.Sessions
            .SingleOrDefaultAsync(s => s.Id == command.SessionId && s.UserId == command.UserId, cancellationToken);

        // An operator viewing as this person is bound to the account they chose to view: switching
        // would re-issue an ordinary token with the operator's marker dropped, and the viewed
        // person's other accounts were never part of what the operator was shown.
        if (session is null || session.ViewAs is not null || !session.IsActive(_clock.UtcNow))
        {
            return Result<ActiveAccountDto>.Fail(Error.Of(nameof(ErrorMessages.SessionNotFound), ErrorType.NotFound));
        }

        var user = await _dbContext.Users.SingleOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);

        // The question every token-issuing path asks (User.MayAuthenticate): a switch mints a working
        // access token, so it is a way in like a refresh is, and a suspended login holding a live
        // session must not obtain a fresh token through it.
        if (user is null || !user.MayAuthenticate)
        {
            return Result<ActiveAccountDto>.Fail(
                Error.Of(nameof(ErrorMessages.InvalidCredentialsUnauthorized), ErrorType.Unauthorized));
        }

        session.SwitchAccount(command.AccountId);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var accessToken = await _accessTokenIssuer.IssueAsync(user, session.Id, command.AccountId, cancellationToken);

        await _journal.RecordIdentifiedAsync(
            command.UserId,
            AuditActions.ActiveAccountSwitched,
            command.AccountId.ToString(),
            command.IpAddress,
            command.UserAgent,
            succeeded: true,
            cancellationToken);

        return Result<ActiveAccountDto>.Ok(
            new ActiveAccountDto(accessToken.Value, accessToken.ExpiresAt, command.AccountId));
    }
}
