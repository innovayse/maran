namespace Maran.Modules.Accounts.Common;

/// <summary>
/// The wire shape of what a plan allows an account to do with mail (issue #177): the same four
/// figures in a request that states them and in the answer that reports them.
/// </summary>
/// <param name="MaxMailboxes">The most mailboxes the account may hold; zero sells none.</param>
/// <param name="DefaultMailboxQuotaMib">The quota in mebibytes a new mailbox gets when the customer names none.</param>
/// <param name="MailboxSendLimitPerHour">The most messages one mailbox may send in an hour.</param>
/// <param name="MailboxQuotaPoolMib">The most mebibytes the quotas of all the account's mailboxes may add up to.</param>
public sealed record MailLimitsDto(
    int MaxMailboxes,
    int DefaultMailboxQuotaMib,
    int MailboxSendLimitPerHour,
    int MailboxQuotaPoolMib);
