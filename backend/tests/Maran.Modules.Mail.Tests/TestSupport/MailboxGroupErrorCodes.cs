namespace Maran.Modules.Mail.Tests.TestSupport;

/// <summary>
/// The error codes the mailbox and autoresponder operations raise, spelled once for the tests that
/// assert them.
/// </summary>
/// <remarks>
/// The module's own code says <c>nameof(ErrorMessages.X)</c>, but that generated class is
/// <c>internal</c> and a test project cannot name it. The spelled strings are the resx keys, so a drift
/// would show as a resource census failure and not pass silently.
/// </remarks>
public static class MailboxGroupErrorCodes
{
    /// <summary>Mail is not enabled on this server.</summary>
    public const string MailNotEnabled = "MailNotEnabled";

    /// <summary>The account could not be found.</summary>
    public const string AccountNotFound = "AccountNotFound";

    /// <summary>The mail domain does not exist for this caller.</summary>
    public const string MailDomainNotFound = "MailDomainNotFound";

    /// <summary>The mailbox does not exist for this caller.</summary>
    public const string MailboxNotFound = "MailboxNotFound";

    /// <summary>The address is already used by a mailbox or an alias.</summary>
    public const string MailboxTaken = "MailboxTaken";

    /// <summary>The account's plan allows no more mailboxes.</summary>
    public const string MailboxLimitReached = "MailboxLimitReached";

    /// <summary>The plan filled up while the mailbox was being created.</summary>
    public const string MailboxLimitReachedConcurrently = "MailboxLimitReachedConcurrently";

    /// <summary>The account's quota pool cannot hold the requested size.</summary>
    public const string MailQuotaPoolExhausted = "MailQuotaPoolExhausted";

    /// <summary>The hourly send limit is above what the plan allows.</summary>
    public const string MailboxSendLimitAbovePlan = "MailboxSendLimitAbovePlan";

    /// <summary>The mailbox could not be recorded after the host accepted it.</summary>
    public const string MailboxProvisioningFailed = "MailboxProvisioningFailed";

    /// <summary>The host does not verify the scheme the panel hashes with.</summary>
    public const string MailPasswordSchemeUnsupported = "MailPasswordSchemeUnsupported";

    /// <summary>The mailbox has no automatic reply.</summary>
    public const string MailAutoresponderNotFound = "MailAutoresponderNotFound";

    /// <summary>The automatic reply is not acceptable.</summary>
    public const string MailAutoresponderInvalid = "MailAutoresponderInvalid";

    /// <summary>The local part is not a valid one.</summary>
    public const string MailboxLocalPartInvalid = "MailboxLocalPartInvalid";

    /// <summary>The password does not satisfy the policy.</summary>
    public const string MailPasswordTooWeak = "MailPasswordTooWeak";

    /// <summary>The quota is outside the allowed range.</summary>
    public const string MailboxQuotaOutOfRange = "MailboxQuotaOutOfRange";

    /// <summary>The send limit is outside the allowed range.</summary>
    public const string MailboxSendLimitOutOfRange = "MailboxSendLimitOutOfRange";

    /// <summary>The domain name filter is longer than a domain name may be.</summary>
    public const string MailDomainTooLong = "MailDomainTooLong";

    /// <summary>The agent could not be reached.</summary>
    public const string AgentUnavailable = "AgentUnavailable";

    /// <summary>The host does not hold what was asked about.</summary>
    public const string AgentNotFound = "AgentNotFound";
}
