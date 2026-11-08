namespace Maran.Sdk.Contracts;

/// <summary>
/// What an account's plan allows the account to do with mail: how many mailboxes it may hold, how large
/// one mailbox is by default, how fast one mailbox may send, and how much disk all of its mailboxes may
/// be given together.
/// </summary>
/// <remarks>
/// <para>
/// Four figures that are read together at one moment (creating or resizing a mailbox), so they travel as
/// one value and not as four loose members of <see cref="AccountSnapshot"/>.
/// </para>
/// <para>
/// <see cref="MailboxQuotaPoolMib"/> is a ceiling on the SUM of the quotas the account hands to its
/// mailboxes, not a measure of disk in use: a mailbox is given a quota when it is created, so the pool is
/// what stops one account promising more disk than its plan sold, whatever the mailboxes actually hold.
/// </para>
/// </remarks>
/// <param name="MaxMailboxes">The most mailboxes the account may hold; zero means none may be created.</param>
/// <param name="DefaultMailboxQuotaMib">The quota in mebibytes a new mailbox gets when the customer names none.</param>
/// <param name="MailboxSendLimitPerHour">The most messages one mailbox may send in an hour.</param>
/// <param name="MailboxQuotaPoolMib">The most mebibytes the quotas of all the account's mailboxes may add up to.</param>
public sealed record MailLimitsSnapshot(
    int MaxMailboxes,
    int DefaultMailboxQuotaMib,
    int MailboxSendLimitPerHour,
    int MailboxQuotaPoolMib)
{
    /// <summary>
    /// The set a plan carries until its operator states one: what a plan that predates mail reads as,
    /// and what a snapshot nobody filled in reads as. A mailbox of one gibibyte and a hundred messages an
    /// hour are the product's stated defaults; ten mailboxes in a ten gibibyte pool is the modest
    /// allowance a plan gets before an operator chooses.
    /// </summary>
    public static readonly MailLimitsSnapshot Default = new(10, 1024, 100, 10240);
}
