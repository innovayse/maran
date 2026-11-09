namespace Maran.Modules.Notifications.Domain.Enums;

/// <summary>A way an operator alert leaves the panel, and therefore a way it can fail to arrive.</summary>
/// <remarks>
/// Stored as its number in the key of <c>AlertChannelState</c>, so the values are fixed for ever.
/// Until the webhook sender exists nothing writes a <see cref="Webhook"/> row, and the dashboard reads
/// that channel as "unknown" (never "ok": nothing has travelled it).
/// </remarks>
public enum AlertChannel
{
    /// <summary>An alert sent as mail to the alert recipients.</summary>
    Mail = 1,

    /// <summary>An alert posted to the operator's webhook.</summary>
    Webhook = 2,
}
