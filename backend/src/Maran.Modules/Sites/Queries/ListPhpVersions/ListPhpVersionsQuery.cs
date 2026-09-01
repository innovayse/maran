namespace Maran.Modules.Sites.Queries.ListPhpVersions;

/// <summary>
/// Lists the PHP versions installed on this server — the reference data a site's backend form
/// selects from, so the customer never has to know or type a version
/// (rules/architecture.md "The backend owns the data, the SPA renders it").
/// </summary>
public sealed record ListPhpVersionsQuery;
