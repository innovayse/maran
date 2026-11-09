namespace Maran.Modules.Dns.Commands.DeleteZone;

/// <summary>
/// Stops serving a zone and removes it, with every record in it.
/// </summary>
/// <param name="ZoneId">
/// Which zone to remove. A row identifier and never a name: the row is what says who owns the zone, and
/// another tenant's identifier answers "not found".
/// </param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record DeleteZoneCommand(Guid ZoneId, string IpAddress, string UserAgent);
