using Maran.Sdk.Contracts;

namespace Maran.Modules.Notifications.Common;

/// <summary>One feed entry as the bell shows it, already worded in the reader's language.</summary>
/// <param name="Id">The entry, the only identifier a dismissal names.</param>
/// <param name="Source">Which part of the panel raised it.</param>
/// <param name="Severity">How much it matters.</param>
/// <param name="Title">The headline sentence, in the request's culture.</param>
/// <param name="Detail">A second line (a task's failure reason, an alert's figure), or <c>null</c>.</param>
/// <param name="DeepLink">A route name from the closed set the SPA maps to a page, or <c>null</c>.</param>
/// <param name="OccurredAt">When the thing happened.</param>
public sealed record NotificationDto(
    Guid Id,
    NotificationSource Source,
    NotificationSeverity Severity,
    string Title,
    string? Detail,
    string? DeepLink,
    DateTimeOffset OccurredAt);
