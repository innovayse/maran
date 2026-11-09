namespace Maran.Modules.Mail.Domain.Policies;

/// <summary>
/// The figures the mail module states once: the ceilings every request is held to and the starting
/// values a server is given when an administrator names none.
/// </summary>
/// <remarks>
/// <para>
/// Each ceiling is the agent's own (<c>agent-core/src/validation/mail</c>), restated here so the panel
/// answers a customer with a typed refusal instead of the agent's. The agent re-validates every value
/// regardless, so a drift between the two costs a worse message and never a weaker check. The starting
/// values are the owner's stated defaults for the product (message size, spam scores, per-mailbox
/// quota and send limit).
/// </para>
/// </remarks>
public static class MailDefaults
{
    /// <summary>The largest message the MTA accepts unless an administrator chooses otherwise, in MiB.</summary>
    public const int MessageSizeMib = 50;

    /// <summary>The largest message size an administrator may choose, in MiB; the agent refuses more.</summary>
    public const int MaxMessageSizeMib = 512;

    /// <summary>The spam score at which a message is greylisted unless an administrator chooses otherwise.</summary>
    public const int SpamGreylistScore = 4;

    /// <summary>The spam score at which a message is marked as spam unless an administrator chooses otherwise.</summary>
    public const int SpamAddHeaderScore = 6;

    /// <summary>The spam score at which a message is refused unless an administrator chooses otherwise.</summary>
    public const int SpamRejectScore = 15;

    /// <summary>The highest spam score an administrator may state; the agent refuses more.</summary>
    public const int MaxSpamScore = 100;

    /// <summary>The longest local part of an address, in characters.</summary>
    public const int LocalPartMaxLength = 64;

    /// <summary>The longest mail domain, in characters, so that every address under it still fits (RFC 5321).</summary>
    public const int DomainMaxLength = 189;

    /// <summary>The largest mailbox quota, in MiB (one tebibyte); the agent refuses more.</summary>
    public const int MaxMailboxQuotaMib = 1_048_576;

    /// <summary>The most messages one mailbox may send in an hour; the agent refuses more.</summary>
    public const int MaxSendLimitPerHour = 100_000;

    /// <summary>The most targets one alias, forwarder or catch-all may have; the agent refuses more.</summary>
    public const int MaxAliasTargets = 20;

    /// <summary>The shortest mailbox password, in characters.</summary>
    public const int PasswordMinLength = 12;

    /// <summary>The longest mailbox password, in characters, so the hashing work stays bounded.</summary>
    public const int PasswordMaxLength = 128;

    /// <summary>The longest autoresponder subject, in characters; the agent refuses more.</summary>
    public const int AutoresponderSubjectMaxLength = 200;

    /// <summary>The longest autoresponder body, in bytes of UTF-8; the agent refuses more.</summary>
    public const int AutoresponderBodyMaxBytes = 4096;

    /// <summary>The longest span, in days, between two replies to the same sender.</summary>
    public const int AutoresponderMaxReplyIntervalDays = 30;

    /// <summary>The size of the RSA key a DKIM signature is made with, in bits.</summary>
    public const int DkimKeyBits = 2048;

    /// <summary>The time to live, in seconds, suggested for the records of a domain.</summary>
    public const int DnsTtlSeconds = 3600;

    /// <summary>The mail ports the daemons are expected to listen on, which an operator must open.</summary>
    /// <remarks>
    /// Informational: the panel opens none of them (rules/security.md item 10). The list is what the
    /// server screen tells an operator to allow through the firewall; what the daemons ACTUALLY listen on
    /// is read back from the host by the status call and shown beside it.
    /// </remarks>
    public static readonly IReadOnlyList<int> ExpectedPorts = [25, 465, 587, 143, 993, 110, 995];
}
