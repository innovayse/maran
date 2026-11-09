using Maran.Modules.Notifications.Common;
using Maran.Modules.Notifications.Domain.Entities;

namespace Maran.Modules.Notifications.Mappers;

/// <summary>Restates a stored notification and its already-resolved words as the wire shape.</summary>
public static class NotificationMapper
{
    /// <summary>Translates one entry.</summary>
    /// <param name="notification">The stored entry.</param>
    /// <param name="title">The headline, resolved for the reader.</param>
    /// <param name="detail">The second line, resolved for the reader, or <c>null</c>.</param>
    /// <returns>The wire shape.</returns>
    public static NotificationDto From(PanelNotification notification, string title, string? detail)
    {
        return new NotificationDto(
            notification.Id,
            notification.Source,
            notification.Severity,
            title,
            detail,
            notification.DeepLink,
            notification.OccurredAt);
    }
}
