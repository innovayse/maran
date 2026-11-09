namespace Maran.Sdk.Contracts;

/// <summary>How urgently a notification-feed entry asks to be read.</summary>
public enum NotificationSeverity
{
    /// <summary>Good or neutral news: a condition resolved.</summary>
    Info = 1,

    /// <summary>Something did not happen as scheduled, and the next run may fix it.</summary>
    Warning = 2,

    /// <summary>A condition that needs the operator.</summary>
    Critical = 3,
}
