namespace Maran.Modules.Identity.Commands.RefreshSession;

/// <summary>Exchanges a refresh token for a new access token and a new refresh token.</summary>
/// <param name="RefreshToken">The token read from the caller's cookie.</param>
/// <param name="IpAddress">The caller's address, recorded on the replacement session.</param>
/// <param name="UserAgent">The caller's user agent, recorded on the replacement session.</param>
public sealed record RefreshSessionCommand(string RefreshToken, string IpAddress, string UserAgent);
