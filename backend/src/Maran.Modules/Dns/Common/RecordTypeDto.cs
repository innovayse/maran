using Maran.Modules.Dns.Domain.Enums;

namespace Maran.Modules.Dns.Common;

/// <summary>One record type a form offers, with the fields it uses, in the viewer's language.</summary>
/// <param name="Type">The type, as the API takes and returns it.</param>
/// <param name="Label">The type as it is written in a zone, such as <c>AAAA</c>.</param>
/// <param name="Description">One sentence saying what the type is for, in the viewer's language.</param>
/// <param name="UsesPriority">Whether the form shows the priority or preference field.</param>
/// <param name="UsesWeightAndPort">Whether the form shows the weight and port fields.</param>
/// <param name="UsesFlagsAndTag">Whether the form shows the flags and tag fields.</param>
public sealed record RecordTypeDto(
    RecordType Type,
    string Label,
    string Description,
    bool UsesPriority,
    bool UsesWeightAndPort,
    bool UsesFlagsAndTag);
