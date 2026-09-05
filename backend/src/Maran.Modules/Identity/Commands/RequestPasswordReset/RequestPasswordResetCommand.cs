namespace Maran.Modules.Identity.Commands.RequestPasswordReset;

/// <summary>Asks the panel to send a password-reset link to one address.</summary>
/// <param name="Email">The address the caller typed. It may belong to nobody; that is not an error.</param>
/// <param name="IpAddress">The caller's address, recorded in the journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the journal.</param>
public sealed record RequestPasswordResetCommand(string Email, string IpAddress, string UserAgent);
