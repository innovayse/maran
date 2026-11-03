namespace Maran.Modules.Identity.Queries.ListAuditOutcomeChoices;

/// <summary>
/// Lists the outcomes the audit journal can be filtered by, for the journal's outcome filter:
/// reference data the filter selects from, produced by the panel rather than worded by the SPA
/// (rules/architecture.md "The backend owns the data, the SPA renders it").
/// </summary>
/// <remarks>
/// The whole closed set, in the order the enum declares it. The journal query still validates the
/// value it is given against the same enum, so this list is advice and the endpoint is the boundary.
/// </remarks>
public sealed record ListAuditOutcomeChoicesQuery;
