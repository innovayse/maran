namespace Maran.Modules.Identity.Controllers.Requests;

/// <summary>The body of a request finishing a sign-in that stopped for a second factor.</summary>
/// <param name="Username">The login name from the first step.</param>
/// <param name="Password">The password from the first step.</param>
/// <param name="Code">A code from the authenticator app, or a recovery code.</param>
public sealed record VerifyTwoFactorRequest(string Username, string Password, string Code);
