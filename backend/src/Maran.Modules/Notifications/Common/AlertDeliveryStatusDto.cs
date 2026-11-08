namespace Maran.Modules.Notifications.Common;

/// <summary>Whether the panel's operator alerts are reaching their destination, per channel.</summary>
/// <param name="Mail">The alert mail channel.</param>
/// <param name="Webhook">The alert webhook channel; <c>unknown</c> until a webhook has been used.</param>
public sealed record AlertDeliveryStatusDto(AlertChannelStatusDto Mail, AlertChannelStatusDto Webhook);
