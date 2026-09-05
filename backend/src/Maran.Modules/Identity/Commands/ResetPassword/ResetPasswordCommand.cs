namespace Maran.Modules.Identity.Commands.ResetPassword;

/// <summary>Sets a new password using a reset token, without knowing the old one.</summary>
/// <param name="Token">The plaintext token from the reset mail. Never logged, never audited.</param>
/// <param name="NewPassword">The new plaintext password. Never logged, never stored, never audited.</param>
/// <param name="IpAddress">The caller's address, recorded in the journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the journal.</param>
public sealed record ResetPasswordCommand(string Token, string NewPassword, string IpAddress, string UserAgent);
