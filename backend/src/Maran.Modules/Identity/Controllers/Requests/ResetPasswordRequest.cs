namespace Maran.Modules.Identity.Controllers.Requests;

/// <summary>The body of a password reset.</summary>
/// <param name="Token">The plaintext token from the reset mail. Never logged and never audited.</param>
/// <param name="NewPassword">The new plaintext password. Never logged, never stored, never audited.</param>
public sealed record ResetPasswordRequest(string Token, string NewPassword);
