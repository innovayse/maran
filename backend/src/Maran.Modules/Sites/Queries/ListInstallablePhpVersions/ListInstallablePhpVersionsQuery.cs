namespace Maran.Modules.Sites.Queries.ListInstallablePhpVersions;

/// <summary>
/// Lists the PHP versions this server can install, installed ones flagged — the reference data the
/// operator's install picker selects from, so a version is never typed
/// (rules/architecture.md "The backend owns the data, the SPA renders it").
/// </summary>
public sealed record ListInstallablePhpVersionsQuery;
