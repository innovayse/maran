namespace Maran.Modules.Identity.Domain.ValueObjects;

/// <summary>
/// A signed access token on its way to the caller. Lives only in memory and in the login response;
/// nothing stores it, which is what makes a fifteen-minute lifetime meaningful.
/// </summary>
/// <param name="Value">The compact-serialized JWT.</param>
/// <param name="ExpiresAt">
/// When it stops being accepted. Returned to the SPA so it can refresh before a call fails, rather
/// than learning the expiry by parsing a token it is not supposed to interpret.
/// </param>
/// <param name="RequiresTwoFactorSetup">
/// Whether this token's holder is being steered into two-factor enrolment and may reach nothing
/// else. It travels beside the token rather than being recomputed by each caller so that the flag
/// in the response body and the claim inside the token are, by construction, the same decision:
/// a body saying "you are free" over a token the authorization handler refuses everywhere would be
/// an unexplainable 403 on every screen.
/// </param>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt, bool RequiresTwoFactorSetup);
