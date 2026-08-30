namespace Maran.Modules.Identity.Common;

/// <summary>
/// The body of a login response. Deliberately has no refresh-token field: that token goes to an
/// httpOnly cookie the page's JavaScript can never read, and a copy in the JSON body would undo
/// exactly the protection the cookie exists for (spec §10).
/// </summary>
/// <param name="AccessToken">The signed access token, or null when a second factor is still owed.</param>
/// <param name="ExpiresAt">When that token expires, so the SPA can refresh before a call fails.</param>
/// <param name="TwoFactorRequired">True when the password was right but a second factor is required.</param>
/// <param name="User">Who signed in, or null while the sign-in is incomplete.</param>
public sealed record LoginResultDto(
    string? AccessToken,
    DateTimeOffset? ExpiresAt,
    bool TwoFactorRequired,
    AuthenticatedUserDto? User);
