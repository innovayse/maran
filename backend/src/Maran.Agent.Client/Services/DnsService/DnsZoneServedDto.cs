namespace Maran.Agent.Client.Services.DnsService;

/// <summary>What the daemon serves for one zone.</summary>
/// <param name="Name">The zone's apex name.</param>
/// <param name="Serial">The SOA serial the daemon serves, as the panel sent it.</param>
/// <param name="Signing">
/// What the daemon was seen to do about signing the zone, or null when the answer carried no observation (a
/// publication's answer never does, and an agent older than the signing contract sends none).
/// </param>
public sealed record DnsZoneServedDto(string Name, long Serial, DnsSigningObservedDto? Signing = null);
