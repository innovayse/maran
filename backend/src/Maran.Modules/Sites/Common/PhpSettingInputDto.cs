namespace Maran.Modules.Sites.Common;

/// <summary>One setting a customer sends: a name and a value in the wire spelling.</summary>
/// <param name="Name">The setting's name, e.g. <c>memory_limit</c>.</param>
/// <param name="Value">The value: mebibytes for a size, digits for seconds and counts, a zone name for a timezone.</param>
public sealed record PhpSettingInputDto(string Name, string Value);
