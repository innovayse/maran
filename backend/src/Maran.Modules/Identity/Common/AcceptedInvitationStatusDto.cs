namespace Maran.Modules.Identity.Common;

/// <summary>
/// The owner has set a password, so there is no invitation left to hand over
/// (<c>state</c> is <c>accepted</c>).
/// </summary>
/// <param name="StateDisplayName">The state named for the request's culture.</param>
public sealed record AcceptedInvitationStatusDto(string StateDisplayName) : InvitationStatusDto(StateDisplayName);
