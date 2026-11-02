using System.Text.Json.Serialization;

namespace Maran.Modules.Identity.Common;

/// <summary>
/// Where a hosting account's owner stands in their invitation. Exactly one of four shapes, told apart
/// by <c>state</c>, so the expiry exists only on the shape that has one. Carries no token, no digest
/// and no link: it is what the panel is allowed to say without minting anything.
/// </summary>
/// <remarks>
/// The primary constructor of an abstract record is protected; the four shapes below are the whole
/// set this module declares.
/// </remarks>
/// <param name="StateDisplayName">
/// The state named for the request's culture. The state stays on the wire as <c>state</c> for scripts
/// and the SPA's branching; this is the text a screen shows (rules/architecture.md, "The backend
/// owns the data, the SPA renders it").
/// </param>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "state")]
[JsonDerivedType(typeof(NoInvitationStatusDto), "none")]
[JsonDerivedType(typeof(PendingInvitationStatusDto), "pending")]
[JsonDerivedType(typeof(ExpiredInvitationStatusDto), "expired")]
[JsonDerivedType(typeof(AcceptedInvitationStatusDto), "accepted")]
public abstract record InvitationStatusDto(string StateDisplayName);
