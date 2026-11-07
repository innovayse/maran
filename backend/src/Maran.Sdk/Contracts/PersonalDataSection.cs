namespace Maran.Sdk.Contracts;

/// <summary>A named group of rows one module holds about a subject, for example a module's sites or sessions.</summary>
/// <param name="Module">The module that holds the rows, as its short name (<c>Identity</c>, <c>Sites</c>).</param>
/// <param name="Name">What the rows are, for example <c>sessions</c> or <c>hostnames</c>.</param>
/// <param name="Records">The rows, in a stable order.</param>
public sealed record PersonalDataSection(string Module, string Name, IReadOnlyList<PersonalDataRecord> Records);
