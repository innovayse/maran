namespace Maran.Modules.Identity.Common;

/// <summary>
/// The owner was invited but every token they were given has lapsed or been replaced
/// (<c>state</c> is <c>expired</c>).
/// </summary>
/// <param name="StateDisplayName">The state named for the request's culture.</param>
public sealed record ExpiredInvitationStatusDto(string StateDisplayName) : InvitationStatusDto(StateDisplayName);
