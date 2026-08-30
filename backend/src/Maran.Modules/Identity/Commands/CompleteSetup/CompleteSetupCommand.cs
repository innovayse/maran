namespace Maran.Modules.Identity.Commands.CompleteSetup;

/// <summary>Creates the panel's first administrator, using the installer's one-time token.</summary>
/// <param name="Token">The token from the installer's one-time link.</param>
/// <param name="Username">The administrator's login name.</param>
/// <param name="Email">The administrator's contact address.</param>
/// <param name="Password">The plaintext password. Never logged, never audited.</param>
/// <param name="IpAddress">The caller's address, recorded in the journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the journal.</param>
public sealed record CompleteSetupCommand(
    string Token,
    string Username,
    string Email,
    string Password,
    string IpAddress,
    string UserAgent);
