namespace Maran.Modules.Identity.Common;

/// <summary>What a caller who has just accepted an invitation is told.</summary>
/// <param name="Username">The login's username, which the person signs in with next. Only a caller
/// holding a usable token ever receives it, and that token is spent by the same request.</param>
public sealed record AcceptInvitationResultDto(string Username);
