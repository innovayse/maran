
namespace Maran.Modules.Identity.Commands.Login;

/// <summary>Signs a user in with a username and password.</summary>
/// <param name="Username">The login name supplied by the caller.</param>
/// <param name="Password">The plaintext password. Never logged, never stored, never audited.</param>
/// <param name="IpAddress">The caller's address, recorded on the session and in the journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded on the session and in the journal.</param>
public sealed record LoginCommand(string Username, string Password, string IpAddress, string UserAgent);
