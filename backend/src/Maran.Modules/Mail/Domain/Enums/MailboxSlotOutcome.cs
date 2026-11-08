namespace Maran.Modules.Mail.Domain.Enums;

/// <summary>What the atomic claim on an account's mailbox allowance answered.</summary>
public enum MailboxSlotOutcome
{
    /// <summary>The claim succeeded and the change was committed.</summary>
    Taken,

    /// <summary>The account already holds as many mailboxes as its plan allows.</summary>
    MailboxLimitReached,

    /// <summary>The quotas of the account's mailboxes would add up to more than its plan's pool.</summary>
    QuotaPoolExhausted,
}
