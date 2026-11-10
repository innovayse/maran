namespace Maran.Modules.Dns.Commands.WithdrawZoneSigning;

/// <summary>
/// Begins withdrawing a zone from DNSSEC signing. The zone keeps its key and stays resolvable while the owner
/// removes the DS record at the registrar; it cannot be returned to unsigned for the minimum wait that follows.
/// </summary>
/// <param name="ZoneId">Which zone, from the route.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record WithdrawZoneSigningCommand(Guid ZoneId, string IpAddress, string UserAgent);
