using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Wolverine;

namespace Maran.Modules.Identity.Commands.ResendSubUserInvitation;

/// <summary>
/// Handles <see cref="ResendSubUserInvitationCommand"/>: retires every outstanding invitation token
/// of a still-pending team member and mails a new one, for the owner of the account the member
/// belongs to.
/// </summary>
/// <remarks>
/// <para>
/// <b>The account is the caller's token, never a parameter.</b> The only id in the request is the
/// sub-user's login, and it is looked up among the members of the account the owner's token speaks
/// for (<see cref="SubUserOwnerGuard"/>). A login of another account, an owner, an administrator and
/// an id that does not exist all answer the same way (<c>SubUserNotFound</c>, 404), so the action
/// cannot be used to find out who belongs to another tenant (rules/security.md item 6).
/// </para>
/// <para>
/// <b>Only a login that is still <see cref="UserLifecycle.Invited"/> is resent to.</b> A fresh token
/// for a login that has already chosen a password would be a password reset in disguise, outside the
/// reset flow's own limits and tokens; it is refused with <c>SubUserNotPending</c> (409). That answer
/// discloses only the state of a member of the owner's own team.
/// </para>
/// <para>
/// <b>The mail goes to the address stored on the login.</b> The owner typed it once, when inviting;
/// resending cannot redirect it, so the action cannot be used to mail a stranger anything other than
/// what the owner could already send them, and a resend retires the previous token, so at most one
/// link is live. Sending shares the account's <c>invitation-sending</c> rate-limit bucket with
/// inviting.
/// </para>
/// <para>
/// <b>A suspended account is offered no way in</b> (<c>InvitationAccountSuspended</c>), as for the
/// first invitation (<see cref="InvitationIssuer"/>'s remarks).
/// </para>
/// <para>
/// <b>The plaintext token is mailed and nowhere else.</b> It is never journalled, returned or logged;
/// the message that carries it is published after the new token is saved and is not durable
/// (rules/csharp.md "a message carrying a secret is never durable"). The mail is written in the
/// account's language, the same one the first invitation used.
/// </para>
/// </remarks>
public sealed class ResendSubUserInvitationCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Establishes that the caller owns the account they are acting for.</summary>
    private readonly SubUserOwnerGuard _guard;

    /// <summary>Retires the old tokens and mints the new one.</summary>
    private readonly InvitationIssuer _issuer;

    /// <summary>Writes the invitation mail.</summary>
    private readonly InvitationMailComposer _composer;

    /// <summary>The read-only window onto the Accounts module: the account's state and language.</summary>
    private readonly IAccountDirectory _accountDirectory;

    /// <summary>Publishes the mail for the Notifications module to send.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Records the resend.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>The authenticated owner, the actor the journal entry names.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="guard">Establishes that the caller owns the account they are acting for.</param>
    /// <param name="issuer">Retires the old tokens and mints the new one.</param>
    /// <param name="composer">Writes the invitation mail.</param>
    /// <param name="accountDirectory">The read-only window onto the Accounts module.</param>
    /// <param name="bus">Publishes the mail.</param>
    /// <param name="journal">Records the resend.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="currentUser">The authenticated owner.</param>
    public ResendSubUserInvitationCommandHandler(
        IdentityDbContext dbContext,
        SubUserOwnerGuard guard,
        InvitationIssuer issuer,
        InvitationMailComposer composer,
        IAccountDirectory accountDirectory,
        IMessageBus bus,
        IdentityAuditJournal journal,
        IClock clock,
        ICurrentUser currentUser)
    {
        _dbContext = dbContext;
        _guard = guard;
        _issuer = issuer;
        _composer = composer;
        _accountDirectory = accountDirectory;
        _bus = bus;
        _journal = journal;
        _clock = clock;
        _currentUser = currentUser;
    }

    /// <summary>Retires the pending member's outstanding tokens, mints a new one and mails it.</summary>
    /// <param name="command">Which member.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Success once the mail is queued, or a typed refusal.</returns>
    public async Task<Result> HandleAsync(ResendSubUserInvitationCommand command, CancellationToken cancellationToken)
    {
        var owned = await _guard.RequireOwnedAccountAsync(cancellationToken);
        if (!owned.IsSuccess)
        {
            return await RefuseAsync(command, owned.Error!, cancellationToken);
        }

        var membership = await _dbContext.AccountMemberships.SingleOrDefaultAsync(
            candidate => candidate.UserId == command.UserId
                && candidate.AccountId == owned.Value
                && candidate.Role == MembershipRole.Member,
            cancellationToken);
        var user = membership is null
            ? null
            : await _dbContext.Users.SingleOrDefaultAsync(candidate => candidate.Id == command.UserId, cancellationToken);

        if (user is null)
        {
            return await RefuseAsync(
                command, Error.Of(nameof(ErrorMessages.SubUserNotFound), ErrorType.NotFound), cancellationToken);
        }

        if (user.Lifecycle != UserLifecycle.Invited)
        {
            return await RefuseAsync(
                command, Error.Of(nameof(ErrorMessages.SubUserNotPending), ErrorType.Conflict), cancellationToken);
        }

        var account = await _accountDirectory.FindAsync(owned.Value, cancellationToken);
        if (account is null)
        {
            return await RefuseAsync(
                command, Error.Of(nameof(ErrorMessages.InvitationAccountNotFound), ErrorType.NotFound), cancellationToken);
        }

        if (account.State == AccountState.Suspended)
        {
            return await RefuseAsync(
                command, Error.Of(nameof(ErrorMessages.InvitationAccountSuspended), ErrorType.Conflict), cancellationToken);
        }

        var minted = await _issuer.MintAsync(user, _clock.UtcNow, cancellationToken);

        await JournalAsync(command, user.Username, succeeded: true, cancellationToken);

        // Published after the token is saved, to the address stored on the login and never to one the
        // request carried. The token is in the message body for this one hop and is stored nowhere.
        await _bus.PublishAsync(_composer.Compose(user.Email, minted.Token.Reveal(), account.OwnerLanguage));

        return Result.Ok();
    }

    /// <summary>Journals a refused attempt and answers with the refusal.</summary>
    /// <param name="command">The refused request.</param>
    /// <param name="error">Why it was refused.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result> RefuseAsync(
        ResendSubUserInvitationCommand command,
        Error error,
        CancellationToken cancellationToken)
    {
        // The subject is the id the caller named: a refused attempt on somebody else's sub-user still
        // leaves a trace of what was probed.
        await JournalAsync(command, command.UserId.ToString("D"), succeeded: false, cancellationToken);
        return Result.Fail(error);
    }

    /// <summary>Journals the resend with the owner as actor and the sub-user as subject; never the token.</summary>
    /// <param name="command">The request.</param>
    /// <param name="subject">The sub-user's login name, or the id the caller named when none matched.</param>
    /// <param name="succeeded">Whether a new invitation was issued.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>A task that completes once the entry is written.</returns>
    private async Task JournalAsync(
        ResendSubUserInvitationCommand command,
        string subject,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        await _journal.RecordIdentifiedAsync(
            SignedInCallerPolicy.Require(_currentUser).UserId,
            IdentityAuditActions.SubUserInvitationResent,
            subject,
            command.IpAddress,
            command.UserAgent,
            succeeded,
            cancellationToken);
    }
}
