namespace Maran.Modules.Monitoring.Queries.ListPendingUpdateClassifications;

/// <summary>
/// Lists the classifications a pending update can carry, for the filter and the badge of the updates
/// screen: reference data the screen selects from, produced by the panel rather than hard-coded by
/// the browser (rules/architecture.md "The backend owns the data, the SPA renders it").
/// </summary>
public sealed record ListPendingUpdateClassificationsQuery;
