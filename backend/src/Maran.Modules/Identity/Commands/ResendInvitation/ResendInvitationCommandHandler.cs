using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;
using Wolverine;

namespace Maran.Modules.Identity.Commands.ResendInvitation;

/// <summary>
/// Handles <see cref="ResendInvitationCommand"/>: retires every outstanding invitation token for a
/// hosting account's login and issues a new one, for an administrator who wants a lost or expired
/// invitation sent again — creating the login itself first, when <c>AccountCreatedHandler</c> never
/// managed to.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this command exists rather than a plain "new token" endpoint.</b>
/// <c>AccountCreatedHandler</c> is invoked inline by the Accounts module and creates the login and
/// its first invitation token in one transaction; when that call fails after the account row is
/// already committed, the account is left with no login at all
/// (<see cref="Maran.Sdk.Events.AccountCreated"/>'s own remarks name this and name this command as
/// the recovery). The finding of the login, its recreation from <c>Account.OwnerEmail</c>, the
/// refusal of any login that is not <see cref="UserState.Invited"/>, the retirement of the old
/// tokens and the minting of the new one are all <see cref="InvitationIssuer"/>'s — shared with the
/// administrator's "create a link" action, which needs exactly the same work and differs only in
/// where the plaintext goes. This handler's own part is what happens to it: it is mailed.
/// </para>
/// <para>
/// <b>Losing the race to create the login answers success.</b> The issuer reports that a
/// concurrent call won the unique-index race by returning no invitation; the winner's own request
/// issues and mails its own token, so there is nothing left for the loser to do but say so. Unlike
/// <c>AccountCreatedHandler</c> — an event handler with no caller waiting on an answer — this one
/// has an administrator waiting on a result, so the loser answers success rather than merely
/// returning: an invitation is, or is about to be, on its way, which is the true state of the
/// account either way.
/// </para>
/// <para>
/// <b>The plaintext token is mailed and nowhere else.</b> It is never journalled, never returned
/// and never logged; the audit entry names the administrator and the customer and carries no token.
/// </para>
/// </remarks>
public sealed class ResendInvitationCommandHandler
{
    /// <summary>The bus the invitation mail is published on.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Records the resend.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>Renders the invitation mail, shared with <c>AccountCreatedHandler</c>.</summary>
    private readonly InvitationMailComposer _composer;

    /// <summary>Finds or creates the login, retires the old tokens and mints the new one.</summary>
    private readonly InvitationIssuer _issuer;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>
    /// The authenticated administrator issuing this resend — the actor the journal entry must name,
    /// not the invited customer the resend is about.
    /// </summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the handler.</summary>
    /// <param name="bus">The bus the invitation mail is published on.</param>
    /// <param name="journal">Records the resend.</param>
    /// <param name="composer">Renders the invitation mail.</param>
    /// <param name="issuer">Finds or creates the login, retires the old tokens and mints the new one.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="currentUser">The authenticated administrator issuing the resend.</param>
    public ResendInvitationCommandHandler(
        IMessageBus bus,
        IdentityAuditJournal journal,
        InvitationMailComposer composer,
        InvitationIssuer issuer,
        IClock clock,
        ICurrentUser currentUser)
    {
        _bus = bus;
        _journal = journal;
        _composer = composer;
        _issuer = issuer;
        _clock = clock;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Creates the account's login if it is missing, retires every outstanding token for it, issues a
    /// new one and mails it.
    /// </summary>
    /// <param name="command">The account whose owner is being (re)invited.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// Success once the mail is queued — or once a concurrent resend has already queued one, see the
    /// type's remarks — or a typed refusal: no such account, or a login that already has a password.
    /// </returns>
    public async Task<Result<bool>> HandleAsync(ResendInvitationCommand command, CancellationToken cancellationToken)
    {
        var issued = await _issuer.IssueAsync(command.AccountId, _clock.UtcNow, cancellationToken);
        if (!issued.IsSuccess)
        {
            return Result<bool>.Fail(issued.Error!);
        }

        if (issued.Value is not { } invitation)
        {
            // Lost the race to create the login — see the type's remarks. The winner's own
            // request issues and mails its own token, so there is nothing left to do here.
            return Result<bool>.Ok(true);
        }

        // The actor is the ADMINISTRATOR who asked for the resend, not the invited customer the
        // resend is about — RecordIdentifiedAsync only names the actor when its id matches
        // ICurrentUser, so passing the administrator's own id here is what makes the entry read
        // "administrator X resent an invitation to customer Y" rather than naming the customer as
        // having verified themselves, which they never did.
        await _journal.RecordIdentifiedAsync(
            _currentUser.UserId,
            AuditActions.CustomerInvited,
            invitation.Username,
            command.IpAddress,
            command.UserAgent,
            succeeded: true,
            cancellationToken);

        // Published, not invoked, for the same reason AccountCreatedHandler publishes rather than
        // sends: the credential lives in the Notifications module, out of this module's reach.
        await _bus.PublishAsync(_composer.Compose(invitation.OwnerEmail, invitation.Token.Reveal()));

        return Result<bool>.Ok(true);
    }
}
