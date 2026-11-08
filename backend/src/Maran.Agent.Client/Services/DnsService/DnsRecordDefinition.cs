namespace Maran.Agent.Client.Services.DnsService;

/// <summary>One record of a zone, as validated fields. The agent re-validates every one of them.</summary>
/// <param name="Name">The owner name relative to the zone; <c>@</c> is the apex.</param>
/// <param name="Kind">The record type.</param>
/// <param name="Ttl">Time to live in seconds.</param>
/// <param name="Value">The address, target name, text or CAA value, by <paramref name="Kind"/>.</param>
/// <param name="Priority">MX preference or SRV priority; zero for every other type.</param>
/// <param name="Weight">SRV weight; zero for every other type.</param>
/// <param name="Port">SRV port; zero for every other type.</param>
/// <param name="Flags">CAA flags; zero for every other type.</param>
/// <param name="Tag">CAA tag (<c>issue</c>, <c>issuewild</c>, <c>iodef</c>); empty for every other type.</param>
public sealed record DnsRecordDefinition(
    string Name,
    DnsRecordKind Kind,
    int Ttl,
    string Value,
    int Priority,
    int Weight,
    int Port,
    int Flags,
    string Tag);
