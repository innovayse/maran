using Maran.Modules.Dns.Domain.Enums;

namespace Maran.Modules.Dns.Domain.ValueObjects;

/// <summary>A template record that was not created because a variable it needs has no value in the settings.</summary>
/// <param name="Name">The owner name of the record that was skipped.</param>
/// <param name="Type">The type of the record that was skipped.</param>
/// <param name="Variable">The first variable the record needs that had no value.</param>
public sealed record SkippedTemplateRecord(string Name, RecordType Type, TemplateVariable Variable);
