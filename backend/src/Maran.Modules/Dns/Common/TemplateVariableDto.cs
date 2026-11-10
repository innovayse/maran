using Maran.Modules.Dns.Domain.Enums;

namespace Maran.Modules.Dns.Common;

/// <summary>One variable of the closed catalogue a template record's value may name.</summary>
/// <param name="Token">The variable as it is written, braces included: <c>{apex}</c>.</param>
/// <param name="Description">What the variable stands for, in the viewer's language.</param>
/// <param name="RecordTypes">The record types whose value may name the variable.</param>
public sealed record TemplateVariableDto(string Token, string Description, IReadOnlyList<RecordType> RecordTypes);
