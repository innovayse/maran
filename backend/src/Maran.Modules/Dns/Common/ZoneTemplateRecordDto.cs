using Maran.Modules.Dns.Domain.Enums;

namespace Maran.Modules.Dns.Common;

/// <summary>One record of a zone template as the administrator's editor shows it.</summary>
/// <param name="Name">The owner name relative to the zone; <c>@</c> is the apex.</param>
/// <param name="Type">The record type.</param>
/// <param name="TypeLabel">The type as DNS itself spells it (<c>MX</c>), the same in every language.</param>
/// <param name="Ttl">Time to live in seconds, or null when a zone created from the template takes the settings' default.</param>
/// <param name="Value">The address, target host name, text or CAA value, possibly naming template variables.</param>
/// <param name="Priority">MX preference or SRV priority; zero when the type has none.</param>
/// <param name="Weight">SRV weight; zero when the type has none.</param>
/// <param name="Port">SRV port; zero when the type has none.</param>
/// <param name="Flags">CAA flags; zero when the type has none.</param>
/// <param name="Tag">CAA tag; empty when the type has none.</param>
public sealed record ZoneTemplateRecordDto(
    string Name,
    RecordType Type,
    string TypeLabel,
    int? Ttl,
    string Value,
    int Priority,
    int Weight,
    int Port,
    int Flags,
    string Tag);
