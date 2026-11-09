using Maran.Modules.Notifications.Domain.Entities;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Notifications.Queries.ListNotifications;

/// <summary>The orders the feed accepts; the validator refuses any other and the handler orders by exactly these.</summary>
public static class ListNotificationsSorts
{
    /// <summary>Newest first when none is named, ties broken by id.</summary>
    public static readonly SortMap<PanelNotification> Map = new SortMapBuilder<PanelNotification>()
        .By(NotificationSortFields.OccurredAt, notification => notification.OccurredAt)
        .DefaultTo(NotificationSortFields.OccurredAt, SortDirection.Descending)
        .TieBreaker(notification => notification.Id);
}
