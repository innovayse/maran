namespace Maran.Modules.Mail.Common;

/// <summary>One option of a mail form or filter: the stable value and the localized label.</summary>
/// <param name="Value">The enum member's name in camelCase, which is what the API accepts and returns.</param>
/// <param name="Label">The localized name the interface shows.</param>
public sealed record MailChoiceDto(string Value, string Label);
