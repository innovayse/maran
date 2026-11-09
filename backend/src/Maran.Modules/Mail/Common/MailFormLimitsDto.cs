namespace Maran.Modules.Mail.Common;

/// <summary>The bounds a mailbox form validates against, produced by the backend so the interface keeps none.</summary>
/// <param name="MinimumQuotaMib">The smallest mailbox quota, in MiB.</param>
/// <param name="MaximumQuotaMib">The largest mailbox quota the caller may choose, in MiB.</param>
/// <param name="DefaultQuotaMib">The quota a new mailbox is offered.</param>
/// <param name="MinimumPasswordLength">The shortest acceptable mailbox password.</param>
/// <param name="MaximumSendLimitPerHour">The highest hourly send limit the caller may choose.</param>
/// <param name="DefaultSendLimitPerHour">The hourly send limit a new mailbox is offered.</param>
/// <param name="MaximumAliasTargets">The most targets one alias may have.</param>
public sealed record MailFormLimitsDto(
    int MinimumQuotaMib,
    int MaximumQuotaMib,
    int DefaultQuotaMib,
    int MinimumPasswordLength,
    int MaximumSendLimitPerHour,
    int DefaultSendLimitPerHour,
    int MaximumAliasTargets);
