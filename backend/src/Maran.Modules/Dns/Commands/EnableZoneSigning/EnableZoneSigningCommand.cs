namespace Maran.Modules.Dns.Commands.EnableZoneSigning;

/// <summary>
/// Switches DNSSEC signing on for a zone: a key is generated, stored encrypted, and the zone is published
/// signed. The zone's owner and an administrator may ask for it.
/// </summary>
/// <param name="ZoneId">Which zone, from the route.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record EnableZoneSigningCommand(Guid ZoneId, string IpAddress, string UserAgent);
