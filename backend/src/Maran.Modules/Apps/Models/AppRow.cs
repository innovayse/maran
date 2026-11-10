using Maran.Modules.Apps.Domain.Enums;

namespace Maran.Modules.Apps.Models;

/// <summary>
/// One app of the list with the host's answer and the ledger's merged: the carrier the list is searched,
/// sorted and paged over before it becomes a <see cref="Common.AppDto"/>. Never on the wire.
/// </summary>
/// <param name="AppId">The catalogue id.</param>
/// <param name="Name">The app's name in the request's language, which the search matches and the name sort orders by.</param>
/// <param name="Kind">What the app is for.</param>
/// <param name="Version">The host's version, else the ledger's last seen, else empty.</param>
/// <param name="State">The ledger state; installed for an app found on the host with no row.</param>
/// <param name="ServiceStatus">The host's service status; none for an app the host does not have.</param>
/// <param name="InstalledAt">When the panel first saw the app installed, or null.</param>
/// <param name="TaskId">The task of the last install or removal, or null.</param>
public sealed record AppRow(
    string AppId,
    string Name,
    AppKind Kind,
    string Version,
    AppState State,
    AppServiceStatus ServiceStatus,
    DateTimeOffset? InstalledAt,
    Guid? TaskId);
