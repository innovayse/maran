namespace Maran.Host.Security;

/// <summary>
/// The operator named by a view-as token's RFC 8693 <c>act</c> claim, read back and checked for
/// shape (issue #317).
/// </summary>
/// <param name="UserId">The operator's user id.</param>
/// <param name="Username">The operator's login name, for the audit journal.</param>
/// <param name="SessionId">The operator's own session the view-as session was started from.</param>
public sealed record ActorClaim(Guid UserId, string Username, Guid SessionId);
