namespace Maran.Agent.Client.Services.DnsService;

/// <summary>What the daemon serves for one zone.</summary>
/// <param name="Name">The zone's apex name.</param>
/// <param name="Serial">The SOA serial the daemon serves, as the panel sent it.</param>
public sealed record DnsZoneServedDto(string Name, long Serial);
