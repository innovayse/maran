namespace Maran.Modules.Identity.Commands.RevokeSession;

/// <summary>Ends one named session of the calling user.</summary>
/// <param name="SessionId">The session to end, chosen from the caller's own list.</param>
/// <param name="UserId">The caller, from their token. The session must belong to them.</param>
/// <param name="IpAddress">The caller's address, recorded in the journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the journal.</param>
public sealed record RevokeSessionCommand(Guid SessionId, Guid UserId, string IpAddress, string UserAgent);
