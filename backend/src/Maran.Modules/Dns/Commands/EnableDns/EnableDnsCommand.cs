namespace Maran.Modules.Dns.Commands.EnableDns;

/// <summary>
/// Switches the name server on: the agent configures and starts it and opens port 53. Refused until the name
/// servers and the SOA mailbox are stated, because every zone names them.
/// </summary>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record EnableDnsCommand(string IpAddress, string UserAgent);
