namespace Maran.Modules.Identity.Models;

/// <summary>A fresh invitation was minted by this call.</summary>
/// <param name="Invitation">The invitation, with the plaintext token this call alone holds.</param>
public sealed record InvitationIssued(IssuedInvitation Invitation) : InvitationIssueOutcome;
