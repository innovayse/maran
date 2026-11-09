using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;
using Wolverine;

namespace Maran.Modules.Identity.Commands.ResendResellerInvitation;

/// <summary>
/// Handles <see cref="ResendResellerInvitationCommand"/>: retires every outstanding invitation token
/// of a reseller's login and mails a new one, for an administrator whose reseller lost or let expire
/// the first invitation.
/// </summary>
/// <remarks>
/// <para>
/// The refusals and the minting are <see cref="ResellerInvitationIssuer"/>'s, shared with the
/// administrator's "create a link" action; this handler's own part is what happens to the plaintext:
/// it is mailed, in the reseller contact's language, to the login's own stored address.
/// </para>
/// <para>
/// <b>The plaintext token is mailed and nowhere else.</b> It is never journalled, returned or logged;
/// the audit entry names the administrator and the reseller's login and carries no token. The mail is
/// published, not sent, for the reason <c>ResellerCreatedHandler</c> gives: the credential lives in
/// the Notifications module, out of this module's reach.
/// </para>
/// </remarks>
public sealed class ResendResellerInvitationCommandHandler
{
    /// <summary>The bus the invitation mail is published on.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Records the resend.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>Renders the invitation mail, shared with <c>ResellerCreatedHandler</c>.</summary>
    private readonly InvitationMailComposer _composer;

    /// <summary>Finds the login, retires the old tokens and mints the new one.</summary>
    private readonly ResellerInvitationIssuer _issuer;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>The authenticated administrator issuing this resend — the actor the journal entry names.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the handler.</summary>
    /// <param name="bus">The bus the invitation mail is published on.</param>
    /// <param name="journal">Records the resend.</param>
    /// <param name="composer">Renders the invitation mail.</param>
    /// <param name="issuer">Finds the login, retires the old tokens and mints the new one.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="currentUser">The authenticated administrator issuing the resend.</param>
    public ResendResellerInvitationCommandHandler(
        IMessageBus bus,
        IdentityAuditJournal journal,
        InvitationMailComposer composer,
        ResellerInvitationIssuer issuer,
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

    /// <summary>Retires every outstanding token for the reseller's login, issues a new one and mails it.</summary>
    /// <param name="command">The reseller whose login is being (re)invited.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Success once the mail is queued, or a typed refusal (see <see cref="ResellerInvitationIssuer"/>).</returns>
    public async Task<Result> HandleAsync(ResendResellerInvitationCommand command, CancellationToken cancellationToken)
    {
        var issued = await _issuer.IssueAsync(command.ResellerId, _clock.UtcNow, cancellationToken);
        if (!issued.IsSuccess)
        {
            return Result.Fail(issued.Error!);
        }

        var invitation = issued.Value!;

        // The actor is the ADMINISTRATOR who asked, not the reseller the resend is about:
        // RecordIdentifiedAsync only names the actor when its id matches ICurrentUser.
        await _journal.RecordIdentifiedAsync(
            SignedInCallerPolicy.Require(_currentUser).UserId,
            IdentityAuditActions.ResellerInvited,
            invitation.Username,
            command.IpAddress,
            command.UserAgent,
            succeeded: true,
            cancellationToken);

        await _bus.PublishAsync(
            _composer.ComposeForReseller(invitation.RecipientEmail, invitation.Token.Reveal(), invitation.RecipientLanguage));

        return Result.Ok();
    }
}
