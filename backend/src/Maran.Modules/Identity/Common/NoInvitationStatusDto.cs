namespace Maran.Modules.Identity.Common;

/// <summary>The account has no panel login yet (<c>state</c> is <c>none</c>).</summary>
/// <param name="StateDisplayName">The state named for the request's culture.</param>
public sealed record NoInvitationStatusDto(string StateDisplayName) : InvitationStatusDto(StateDisplayName);
