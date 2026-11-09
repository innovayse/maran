namespace Maran.Modules.Notifications.Jobs;

/// <summary>
/// The scheduled trigger for one retention pass over <c>notifications.PanelNotifications</c>. Carries
/// no parameters: what has aged out is decided by <see cref="NotificationRetentionHandler"/> and the
/// injected clock, never by whoever scheduled it.
/// </summary>
/// <remarks>A message rather than a timer callback, for the reason <c>TaskRetentionRequested</c> is one.</remarks>
public sealed record NotificationRetentionRequested;
