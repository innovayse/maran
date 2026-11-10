namespace Maran.Agent.Client.Services.DnsService;

/// <summary>A zone as the panel hands it to the agent: its identity, its SOA inputs and its complete record set.</summary>
/// <param name="Name">The zone's apex name.</param>
/// <param name="Serial">The SOA serial, computed by the panel and never derived by the agent.</param>
/// <param name="PrimaryNameserver">The SOA's primary name server.</param>
/// <param name="HostmasterMailbox">The SOA's responsible mailbox, in dotted form.</param>
/// <param name="Nameservers">The apex NS set, system-managed from the name-server settings.</param>
/// <param name="Records">Every other record of the zone.</param>
/// <param name="Signing">The zone's signing instruction, or null for a zone that is not signed.</param>
public sealed record DnsZoneDefinition(
    string Name,
    long Serial,
    string PrimaryNameserver,
    string HostmasterMailbox,
    IReadOnlyList<string> Nameservers,
    IReadOnlyList<DnsRecordDefinition> Records,
    DnsSigningDefinition? Signing = null);
