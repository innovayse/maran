namespace Maran.Modules.Identity.Common;

/// <summary>
/// A signed access token on its way to the caller. Lives only in memory and in the login response;
/// nothing stores it, which is what makes a fifteen-minute lifetime meaningful.
/// </summary>
/// <param name="Value">The compact-serialized JWT.</param>
/// <param name="ExpiresAt">
/// When it stops being accepted. Returned to the SPA so it can refresh before a call fails, rather
/// than learning the expiry by parsing a token it is not supposed to interpret.
/// </param>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);
