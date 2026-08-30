namespace Maran.Modules.Identity.Common;

/// <summary>
/// A freshly issued session and the one and only copy of its refresh token in plaintext.
/// </summary>
/// <param name="SessionId">The session's identity, written into the access token's <c>sid</c> claim.</param>
/// <param name="RefreshToken">
/// The token itself, on its way to an httpOnly cookie. Only the hash is stored, so this value
/// cannot be recovered afterwards — by us or by anyone reading the database.
/// </param>
/// <param name="ExpiresAt">When the refresh token stops being accepted.</param>
public sealed record IssuedSession(Guid SessionId, string RefreshToken, DateTimeOffset ExpiresAt);
