using Maran.Modules.Dns.Domain.Enums;

namespace Maran.Modules.Dns.Common;

/// <summary>One record of a zone as a screen shows it.</summary>
/// <param name="Id">The record's identity, and the only identifier a request may name for it.</param>
/// <param name="ZoneId">The zone the record belongs to.</param>
/// <param name="Name">The owner name relative to the zone; <c>@</c> is the apex.</param>
/// <param name="Type">The record type.</param>
/// <param name="Ttl">Time to live in seconds.</param>
/// <param name="Value">The address, target host name, text or CAA value.</param>
/// <param name="Priority">MX preference or SRV priority; zero when the type has none.</param>
/// <param name="Weight">SRV weight; zero when the type has none.</param>
/// <param name="Port">SRV port; zero when the type has none.</param>
/// <param name="Flags">CAA flags; zero when the type has none.</param>
/// <param name="Tag">CAA tag; empty when the type has none.</param>
/// <param name="Origin">Who put the record into the zone.</param>
/// <param name="OriginName">The origin as a person reads it, in the viewer's language.</param>
/// <param name="Editable">
/// Whether the record may be changed or removed through the record endpoints, which only a customer's own
/// record and one a template created may. A screen shows the others read-only; the server refuses an edit of them either way.
/// </param>
/// <param name="CreatedAt">When the record was created.</param>
public sealed record ZoneRecordDto(
    Guid Id,
    Guid ZoneId,
    string Name,
    RecordType Type,
    int Ttl,
    string Value,
    int Priority,
    int Weight,
    int Port,
    int Flags,
    string Tag,
    RecordOrigin Origin,
    string OriginName,
    bool Editable,
    DateTimeOffset CreatedAt);
