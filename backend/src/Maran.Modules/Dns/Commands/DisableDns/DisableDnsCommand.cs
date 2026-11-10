namespace Maran.Modules.Dns.Commands.DisableDns;

/// <summary>
/// Switches the name server off: the agent stops it. The firewall is not touched: any rule for port 53 stays until the
/// administrator removes it. Zones and their files stay, so switching it on again serves them as they were.
/// </summary>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record DisableDnsCommand(string IpAddress, string UserAgent);
