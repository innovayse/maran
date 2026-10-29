using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Security;

namespace Maran.Modules.Identity.Commands.IssueInvitationLink;

/// <summary>
/// Handles <see cref="IssueInvitationLinkCommand"/>: issues a fresh invitation for a hosting
/// account's owner and hands its link back to the administrator, so an installation that cannot send
/// mail still has a way to let a customer in.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why handing the administrator a link does not weaken the model.</b> The administrator must
/// never know a customer's <i>password</i>, and still does not: the link lets the OWNER choose one.
/// What the link is, is a credential that sets a first password — so the design question is where
/// the plaintext may exist, and the answer is: in this one response and nowhere else.
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <b>Not recovered, re-minted.</b> The database holds only the token's digest, and this command
/// does not change that. It does what a resend does to the token — retires every outstanding one and
/// issues a new one — through the same <see cref="InvitationIssuer"/>, and returns the new plaintext
/// once. A lost link is replaced by asking again; it is never looked up. Keeping a recoverable copy
/// (even encrypted) would turn a leaked table into a way into every unclaimed account.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Administrator-only, by the controller's class-level policy.</b> A customer or an anonymous
/// caller is refused before this handler runs, so the refusal cannot be made to depend on, or
/// disclose anything about, which accounts exist.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Always journalled, never with the link, never logged.</b> The audit entry records that an
/// administrator created a link for a customer, from which address, and nothing of the link — and a
/// REFUSED attempt is recorded too (an unknown account, or a login that already has a password),
/// because an administrator probing for logins that can be taken over is exactly what a journal of a
/// credential-issuing action is for. This handler logs nothing, and the answer carries the link as a
/// <see cref="SensitiveString"/> so a record's printing members cannot leak it by accident.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>A dead link is never offered.</b> The link is not remembered by the server at all; whether
/// there is still an invitation to hand over is a question the status query answers fresh from the
/// token rows each time (<c>GetInvitationStatusQueryHandler</c>), so an accepted or expired
/// invitation stops being offered the moment it is one. The same refusal a resend has applies: a
/// login that already has a password gets no link.
/// </description>
/// </item>
/// </list>
/// <para>
/// <b>The previous link stops working, and that is stated rather than hidden.</b> Creating a link
/// retires the one already mailed, exactly as a resend does; the screen says so before the button
/// is pressed.
/// </para>
/// <para>
/// <b>A lost race to create the login is retried once.</b> When the issuer reports that a concurrent
/// call created the login first, the login now exists, so asking again takes the ordinary path. A
/// second miss means the login has since vanished (its account was deleted between the two reads) and
/// is answered as an account that does not exist.
/// </para>
/// </remarks>
public sealed class IssueInvitationLinkCommandHandler
{
    /// <summary>Finds or creates the login, retires the old tokens and mints the new one.</summary>
    private readonly InvitationIssuer _issuer;

    /// <summary>Builds the address the token is embedded in.</summary>
    private readonly InvitationLinkBuilder _linkBuilder;

    /// <summary>Records that a link was created. Never given the link.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>The authenticated administrator — the actor the journal entry names.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the handler.</summary>
    /// <param name="issuer">Finds or creates the login, retires the old tokens and mints the new one.</param>
    /// <param name="linkBuilder">Builds the address the token is embedded in.</param>
    /// <param name="journal">Records that a link was created.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="currentUser">The authenticated administrator.</param>
    public IssueInvitationLinkCommandHandler(
        InvitationIssuer issuer,
        InvitationLinkBuilder linkBuilder,
        IdentityAuditJournal journal,
        IClock clock,
        ICurrentUser currentUser)
    {
        _issuer = issuer;
        _linkBuilder = linkBuilder;
        _journal = journal;
        _clock = clock;
        _currentUser = currentUser;
    }

    /// <summary>Issues a fresh invitation and returns its link.</summary>
    /// <param name="command">The account whose owner is being invited.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// The one-time link and when it expires, or a typed refusal: no such account, or a login that
    /// already has a password.
    /// </returns>
    public async Task<Result<InvitationLinkDto>> HandleAsync(
        IssueInvitationLinkCommand command,
        CancellationToken cancellationToken)
    {
        var issued = await _issuer.IssueAsync(command.AccountId, _clock.UtcNow, cancellationToken);
        if (issued.IsSuccess && issued.Value is null)
        {
            // Lost the race to create the login — see the type's remarks. It exists now.
            issued = await _issuer.IssueAsync(command.AccountId, _clock.UtcNow, cancellationToken);
        }

        if (!issued.IsSuccess)
        {
            return await RefuseAsync(command, issued.Error!, cancellationToken);
        }

        if (issued.Value is not { } invitation)
        {
            return await RefuseAsync(
                command,
                Error.Of(nameof(Resources.ErrorMessages.InvitationAccountNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        // The subject is the customer's login name — what was acted on — and nothing derived from the
        // token: not the link, not the token, not its digest.
        await _journal.RecordIdentifiedAsync(
            _currentUser.UserId,
            AuditActions.InvitationLinkIssued,
            invitation.Username,
            command.IpAddress,
            command.UserAgent,
            succeeded: true,
            cancellationToken);

        return Result<InvitationLinkDto>.Ok(
            new InvitationLinkDto(
                new SensitiveString(_linkBuilder.Build(invitation.Token.Reveal())),
                invitation.ExpiresAt));
    }

    /// <summary>Journals a refused attempt and answers with the refusal.</summary>
    /// <param name="command">The refused request.</param>
    /// <param name="error">Why it was refused.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<InvitationLinkDto>> RefuseAsync(
        IssueInvitationLinkCommand command,
        Error error,
        CancellationToken cancellationToken)
    {
        // No login was acted on, so the subject is the identifier the caller supplied: a probe for an
        // account that is not there still leaves a trace of what was probed for.
        await _journal.RecordIdentifiedAsync(
            _currentUser.UserId,
            AuditActions.InvitationLinkIssued,
            command.AccountId.ToString("D"),
            command.IpAddress,
            command.UserAgent,
            succeeded: false,
            cancellationToken);

        return Result<InvitationLinkDto>.Fail(error);
    }
}
