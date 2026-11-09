namespace Maran.Modules.Mail.Domain.Enums;

/// <summary>The host services that together make up the mail stack.</summary>
public enum MailDaemonKind
{
    /// <summary>The SMTP server that accepts and sends mail.</summary>
    Mta,

    /// <summary>The IMAP and POP3 server and the mailbox store.</summary>
    ImapServer,

    /// <summary>The spam filter, which also signs outgoing mail and limits senders.</summary>
    SpamFilter,

    /// <summary>The key-value store the spam filter keeps its rate limits in.</summary>
    RateLimitStore,

    /// <summary>The optional virus scanner.</summary>
    VirusScanner,
}
