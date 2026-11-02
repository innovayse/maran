namespace Maran.Modules.Identity.Common;

/// <summary>
/// The owner holds an invitation that still works (<c>state</c> is <c>pending</c>); the only shape
/// with an expiry.
/// </summary>
/// <param name="StateDisplayName">The state named for the request's culture.</param>
/// <param name="ExpiresAt">When the newest usable token stops working.</param>
public sealed record PendingInvitationStatusDto(string StateDisplayName, DateTimeOffset ExpiresAt)
    : InvitationStatusDto(StateDisplayName);
