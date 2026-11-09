using Maran.Modules.Dns.Domain.Enums;

namespace Maran.Modules.Dns.Domain.ValueObjects;

/// <summary>
/// One record as a caller states it, before it is a row: the owner name relative to the zone, the type and
/// the type's fields. It is also the shape that crosses the wire, so a field cannot be added to the form and
/// forgotten in the rule.
/// </summary>
/// <remarks>
/// A field that a type does not use stays at its default, and the shape policy refuses it otherwise, so
/// every field has one meaning for the type that reads it.
/// </remarks>
/// <param name="Name">The owner name relative to the zone; <c>@</c> is the apex, <c>*</c> may lead as a wildcard.</param>
/// <param name="Type">The record type.</param>
/// <param name="Ttl">Time to live in seconds.</param>
/// <param name="Value">The address, target host name, text or CAA value, by type.</param>
/// <param name="Priority">MX preference or SRV priority.</param>
/// <param name="Weight">SRV weight.</param>
/// <param name="Port">SRV port.</param>
/// <param name="Flags">CAA flags.</param>
/// <param name="Tag">CAA tag.</param>
public sealed record RecordDraft(
    string Name,
    RecordType Type,
    int Ttl,
    string Value,
    int Priority = 0,
    int Weight = 0,
    int Port = 0,
    int Flags = 0,
    string Tag = "");
