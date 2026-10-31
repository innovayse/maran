namespace Maran.Modules.Tasks.Queries.ListTaskKindChoices;

/// <summary>
/// Lists the kinds of task the caller can see, for the tasks list's kind filter: reference data the
/// filter selects from, produced by the panel rather than typed by the reader
/// (rules/architecture.md "The backend owns the data, the SPA renders it").
/// </summary>
/// <remarks>
/// The kinds of the rows the caller may SEE, read through the context's query filter: a customer is
/// offered only kinds that occur among their own tasks, so the list itself discloses nothing about an
/// administrator's work, and a kind a marketplace module recorded is offered as soon as it exists.
/// </remarks>
public sealed record ListTaskKindChoicesQuery;
