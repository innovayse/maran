namespace Maran.Modules.Identity.Common;

/// <summary>One value an API key screen offers as a choice: a scope in the create dialog, a status in the list filter.</summary>
/// <param name="Value">The machine-stable value, which is what a request sends back.</param>
/// <param name="Name">The value as an administrator reads it, in the request's culture.</param>
public sealed record ApiKeyChoiceDto(string Value, string Name);
