namespace Maran.Modules.Dns.Queries.GetZoneDnssec;

/// <summary>Reads one zone's DNSSEC situation.</summary>
/// <param name="ZoneId">The zone to read; another tenant's id answers "not found".</param>
public sealed record GetZoneDnssecQuery(Guid ZoneId);
