namespace Maran.Modules.Identity.Queries.ListApiKeyStatuses;

/// <summary>
/// Lists the statuses an API key can be in, for the key list's status filter: the reference data the
/// filter selects from, produced by the panel rather than typed into the SPA
/// (rules/architecture.md "The backend owns the data, the SPA renders it").
/// </summary>
public sealed record ListApiKeyStatusesQuery;
