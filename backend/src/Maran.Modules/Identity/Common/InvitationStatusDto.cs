using Maran.Modules.Identity.Domain.Enums;

namespace Maran.Modules.Identity.Common;

/// <summary>
/// Where a hosting account's owner stands in their invitation. Carries no token, no digest and no
/// link: it is what the panel is allowed to say without minting anything.
/// </summary>
/// <param name="State">Whether an invitation is outstanding, lapsed, accepted or was never issued.</param>
/// <param name="StateDisplayName">
/// <paramref name="State"/> named for the request's culture. The state stays on the wire for scripts
/// and the SPA's branching; this is the text a screen shows (rules/architecture.md, "The backend
/// owns the data, the SPA renders it").
/// </param>
/// <param name="ExpiresAt">
/// When the newest usable token stops working; <c>null</c> unless <paramref name="State"/> is
/// <see cref="InvitationState.Pending"/>.
/// </param>
public sealed record InvitationStatusDto(InvitationState State, string StateDisplayName, DateTimeOffset? ExpiresAt);
