namespace Maran.Modules.Sites.Common;

/// <summary>
/// One option of a closed set a site form or filter selects from: the wire value the API accepts and
/// the name to show for it, already written in the caller's language.
/// </summary>
/// <param name="Value">The value the API accepts and serializes for this option (camel-cased member name).</param>
/// <param name="Label">The option's display name in the caller's language.</param>
public sealed record SiteChoiceDto(string Value, string Label);
