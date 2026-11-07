namespace Maran.Sdk.Contracts;

/// <summary>
/// One row of what a module holds about a subject, as named text fields. The field names are chosen by
/// the provider that builds it, one by one; nothing is copied from an entity wholesale, so a column that
/// is added later is not exported until a provider names it.
/// </summary>
/// <param name="Fields">Field name to its value as text, or <c>null</c> when the row has no value for it.</param>
public sealed record PersonalDataRecord(IReadOnlyDictionary<string, string?> Fields);
