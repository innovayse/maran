using Maran.Modules.Apps.Domain.Enums;

namespace Maran.Modules.Apps.Common;

/// <summary>
/// One app of the panel's app store as an administrator reads it: what it is, where it stands in the panel's
/// ledger, and what its service is doing right now.
/// </summary>
/// <param name="AppId">The catalogue id (<c>nginx</c>, <c>apache</c>, <c>php-8.3</c>, <c>nodejs-22</c>), which every action names.</param>
/// <param name="Name">The app's name for the request's language, e.g. "PHP 8.3".</param>
/// <param name="Kind">What the app is for.</param>
/// <param name="KindName">The kind in the request's language.</param>
/// <param name="Version">The version the host reports; empty while the app is not installed or the host reports none.</param>
/// <param name="State">Where the app stands in the ledger.</param>
/// <param name="StateName">The state in the request's language.</param>
/// <param name="ServiceStatus">What the service manager reports about the app's service, probed at the time of the read.</param>
/// <param name="ServiceStatusName">The service status in the request's language.</param>
/// <param name="InstalledAt">When the panel first saw the app installed, or <see langword="null"/> while it has not.</param>
/// <param name="TaskId">The task of the last install or removal, so its progress and log can be opened; null for an app found on the host.</param>
public sealed record AppDto(
    string AppId,
    string Name,
    AppKind Kind,
    string KindName,
    string Version,
    AppState State,
    string StateName,
    AppServiceStatus ServiceStatus,
    string ServiceStatusName,
    DateTimeOffset? InstalledAt,
    Guid? TaskId);
