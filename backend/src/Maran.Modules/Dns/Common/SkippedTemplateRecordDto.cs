using Maran.Modules.Dns.Domain.Enums;

namespace Maran.Modules.Dns.Common;

/// <summary>A template record a new zone did not get, because a setting its value needs is not stated.</summary>
/// <param name="Name">The owner name of the record that was left out.</param>
/// <param name="Type">The type of the record that was left out.</param>
/// <param name="TypeLabel">The type as DNS itself spells it (<c>AAAA</c>), the same in every language.</param>
/// <param name="Token">The variable that had no value, such as <c>{ipv6}</c>.</param>
/// <param name="Explanation">Why the record was left out, in the viewer's language.</param>
public sealed record SkippedTemplateRecordDto(
    string Name,
    RecordType Type,
    string TypeLabel,
    string Token,
    string Explanation);
