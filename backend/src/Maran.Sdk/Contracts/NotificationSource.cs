namespace Maran.Sdk.Contracts;

/// <summary>Where a notification-feed entry came from.</summary>
public enum NotificationSource
{
    /// <summary>A background task that failed.</summary>
    Task = 1,

    /// <summary>A server alert: monitoring, panel updates, certificate expiry.</summary>
    Alert = 2,

    /// <summary>A backup alert.</summary>
    Backup = 3,
}
