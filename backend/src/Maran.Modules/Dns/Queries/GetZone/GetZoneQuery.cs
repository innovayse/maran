namespace Maran.Modules.Dns.Queries.GetZone;

/// <summary>Reads one zone.</summary>
/// <param name="ZoneId">The zone to read; another tenant's id answers "not found".</param>
public sealed record GetZoneQuery(Guid ZoneId);
