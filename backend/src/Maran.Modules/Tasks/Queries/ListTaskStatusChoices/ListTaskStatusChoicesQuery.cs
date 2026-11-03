namespace Maran.Modules.Tasks.Queries.ListTaskStatusChoices;

/// <summary>
/// Lists the statuses the tasks list can be filtered by, for the list's status filter: reference data
/// the filter selects from, produced by the panel rather than worded by the SPA
/// (rules/architecture.md "The backend owns the data, the SPA renders it").
/// </summary>
/// <remarks>
/// The whole closed set, in the order the enum declares it, the same for every caller: a status
/// discloses nothing about another account's work. The list query still validates the value it is
/// given against the same enum, so this list is advice and the endpoint is the boundary.
/// </remarks>
public sealed record ListTaskStatusChoicesQuery;
