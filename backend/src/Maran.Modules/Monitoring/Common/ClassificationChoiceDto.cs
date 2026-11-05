namespace Maran.Modules.Monitoring.Common;

/// <summary>One classification the pending updates list can be filtered by, as its filter lists it.</summary>
/// <param name="Value">
/// The word a row carries and the <c>classification</c> filter accepts: <c>security</c>,
/// <c>ordinary</c> or <c>unmeasured</c>.
/// </param>
/// <param name="Name">The word as an administrator reads it, resolved for the request's culture.</param>
public sealed record ClassificationChoiceDto(string Value, string Name);
