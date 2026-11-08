using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Security;

namespace Maran.Modules.Identity.Commands.IssueResellerInvitationLink;

/// <summary>
/// Handles <see cref="IssueResellerInvitationLinkCommand"/>: issues a fresh invitation for a
/// reseller's login and hands its link back to the administrator, so a panel that cannot send mail
/// still has a way to let a reseller in.
/// </summary>
/// <remarks>
/// <para>
/// The reasoning is <c>IssueInvitationLinkCommandHandler</c>'s, and it holds unchanged: the
/// administrator never learns a password (the link lets the reseller choose one); the link is
/// re-minted, never recovered, because the database holds only the token's digest; the previous link
/// stops working; the answer is administrator-only by the controller's class-level policy; and a dead
/// link is never offered, because a login that already has a password is refused.
/// </para>
/// <para>
/// <b>Always journalled, never with the link.</b> The entry records that an administrator created a
/// link for a reseller's login, from which address, and nothing of the link. A REFUSED attempt is
/// recorded too, because an administrator probing for logins that can be taken over is what a
/// journal of a credential-issuing action is for. The answer carries the link as a
/// <see cref="SensitiveString"/> so a record's printing members cannot leak it.
/// </para>
/// </remarks>
public sealed class IssueResellerInvitationLinkCommandHandler
{
    /// <summary>Finds the login, retires the old tokens and mints the new one.</summary>
    private readonly ResellerInvitationIssuer _issuer;

    /// <summary>Builds the address the token is embedded in.</summary>
    private readonly InvitationLinkBuilder _linkBuilder;

    /// <summary>Records that a link was created. Never given the link.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>The authenticated administrator — the actor the journal entry names.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the handler.</summary>
    /// <param name="issuer">Finds the login, retires the old tokens and mints the new one.</param>
    /// <param name="linkBuilder">Builds the address the token is embedded in.</param>
    /// <param name="journal">Records that a link was created.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="currentUser">The authenticated administrator.</param>
    public IssueResellerInvitationLinkCommandHandler(
        ResellerInvitationIssuer issuer,
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
    /// <param name="command">The reseller whose login is being invited.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The one-time link and when it expires, or a typed refusal.</returns>
    public async Task<Result<InvitationLinkDto>> HandleAsync(
        IssueResellerInvitationLinkCommand command,
        CancellationToken cancellationToken)
    {
        var issued = await _issuer.IssueAsync(command.ResellerId, _clock.UtcNow, cancellationToken);
        if (!issued.IsSuccess)
        {
            // No login was acted on, so the subject is the identifier the caller supplied: a probe for
            // a reseller that is not there still leaves a trace of what was probed for.
            await _journal.RecordIdentifiedAsync(
                SignedInCallerPolicy.Require(_currentUser).UserId,
                IdentityAuditActions.InvitationLinkIssued,
                command.ResellerId.ToString("D"),
                command.IpAddress,
                command.UserAgent,
                succeeded: false,
                cancellationToken);

            return Result<InvitationLinkDto>.Fail(issued.Error!);
        }

        var invitation = issued.Value!;

        // The subject is the login name — what was acted on — and nothing derived from the token.
        await _journal.RecordIdentifiedAsync(
            SignedInCallerPolicy.Require(_currentUser).UserId,
            IdentityAuditActions.InvitationLinkIssued,
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
}
