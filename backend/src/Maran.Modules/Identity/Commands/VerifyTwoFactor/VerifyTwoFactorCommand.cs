namespace Maran.Modules.Identity.Commands.VerifyTwoFactor;

/// <summary>Finishes a sign-in that stopped for a second factor.</summary>
/// <remarks>
/// The password is presented again rather than carrying a short-lived "half signed in" ticket
/// between the two steps. A ticket would be a second credential to issue, store, expire and revoke,
/// and one that grants the account to whoever holds it; repeating the password keeps exactly one
/// thing to protect and makes the second step verify both factors at once.
/// </remarks>
/// <param name="Username">The login name from the first step.</param>
/// <param name="Password">The password from the first step.</param>
/// <param name="Code">A TOTP code, or one of the user's recovery codes.</param>
/// <param name="IpAddress">The caller's address.</param>
/// <param name="UserAgent">The caller's user agent.</param>
public sealed record VerifyTwoFactorCommand(
    string Username,
    string Password,
    string Code,
    string IpAddress,
    string UserAgent);
