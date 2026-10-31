namespace Maran.Modules.Tasks.Common;

/// <summary>One kind the tasks list can be filtered by, as the filter's choice list offers it.</summary>
/// <param name="Kind">The machine-stable kind, which is what the filter sends back.</param>
/// <param name="KindName">
/// The kind as an operator reads it, resolved for the request's culture; the machine kind itself for
/// one this build carries no entry for.
/// </param>
public sealed record TaskKindChoiceDto(string Kind, string KindName);
