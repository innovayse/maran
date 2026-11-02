namespace Maran.Modules.Identity.Models;

/// <summary>
/// A concurrent call won the race to create the account's login, so nothing was issued here: the
/// winner issues its own token, and what the loser does about that is its caller's decision.
/// </summary>
public sealed record InvitationAlreadyBeingIssued : InvitationIssueOutcome;
