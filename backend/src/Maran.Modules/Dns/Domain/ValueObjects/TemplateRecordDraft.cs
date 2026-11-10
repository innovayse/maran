using Maran.Modules.Dns.Domain.Enums;

namespace Maran.Modules.Dns.Domain.ValueObjects;

/// <summary>
/// One record of a zone template as an administrator states it: the typed fields of a record, whose
/// <paramref name="Value"/> may name the variables <see cref="TemplateVariable"/> lists, and a time to live that
/// may be left to the settings. It becomes a <see cref="RecordDraft"/> only by expansion, and that draft then
/// passes the same shape policy a customer's own record passes.
/// </summary>
/// <param name="Name">The owner name relative to the zone; <c>@</c> is the apex. Never carries a variable.</param>
/// <param name="Type">The record type.</param>
/// <param name="Ttl">Time to live in seconds, or <c>null</c> for the settings' default at expansion.</param>
/// <param name="Value">The address, target host name, text or CAA value, possibly naming variables.</param>
/// <param name="Priority">MX preference or SRV priority.</param>
/// <param name="Weight">SRV weight.</param>
/// <param name="Port">SRV port.</param>
/// <param name="Flags">CAA flags.</param>
/// <param name="Tag">CAA tag.</param>
public sealed record TemplateRecordDraft(
    string Name,
    RecordType Type,
    int? Ttl,
    string Value,
    int Priority = 0,
    int Weight = 0,
    int Port = 0,
    int Flags = 0,
    string Tag = "");
