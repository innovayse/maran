namespace Maran.Modules.Apps.Queries.ListInstallableApps;

/// <summary>
/// Reads the apps the host's package sources can install, installed ones included
/// (<c>GET /api/v1/apps/installable</c>). Carries no parameters: the list is always scoped to this server
/// and is short and closed, so it is not paged.
/// </summary>
public sealed record ListInstallableAppsQuery;
