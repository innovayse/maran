using Maran.Modules.Notifications.Domain.Entities;
using Maran.Sdk.Validators;

namespace Maran.Modules.Notifications.Queries.ListNotifications;

/// <summary>
/// Holds <see cref="ListNotificationsQuery"/> to the panel's paging bounds. The source filter needs
/// no rule of its own: over HTTP the enum binder refuses a number naming no member, and on the bus a
/// value naming none matches no row.
/// </summary>
public sealed class ListNotificationsQueryValidator : PagedQueryValidator<ListNotificationsQuery, PanelNotification>
{
    /// <summary>Declares the paging rules against <see cref="ListNotificationsSorts.Map"/>.</summary>
    public ListNotificationsQueryValidator()
        : base(ListNotificationsSorts.Map)
    {
    }
}
