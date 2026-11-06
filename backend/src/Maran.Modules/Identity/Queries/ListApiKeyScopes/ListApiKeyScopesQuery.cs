namespace Maran.Modules.Identity.Queries.ListApiKeyScopes;

/// <summary>
/// Lists the scopes an API key can be granted, for the create dialog's choice list: the reference data
/// the form selects from, produced by the panel rather than typed by the administrator
/// (rules/architecture.md "The backend owns the data, the SPA renders it").
/// </summary>
public sealed record ListApiKeyScopesQuery;
