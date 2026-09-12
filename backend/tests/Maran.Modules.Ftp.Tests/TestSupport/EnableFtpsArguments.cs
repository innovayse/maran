namespace Maran.Modules.Ftp.Tests.TestSupport;

/// <summary>
/// What the panel actually put on the wire when it asked the agent to enable FTPS, captured by
/// <see cref="RecordingFtpsAgent"/> so a test can assert the VALUES rather than that a call happened.
/// </summary>
/// <param name="Hostname">The hostname the panel asked the daemon to serve.</param>
/// <param name="PassivePortMin">The lowest passive port the panel sent.</param>
/// <param name="PassivePortMax">The highest passive port the panel sent.</param>
/// <param name="PassiveAddress">The PASV address the panel sent, empty when the operator gave none.</param>
/// <param name="MaxClients">The concurrent-session ceiling the panel sent.</param>
public sealed record EnableFtpsArguments(
    string Hostname,
    uint PassivePortMin,
    uint PassivePortMax,
    string PassiveAddress,
    uint MaxClients);
