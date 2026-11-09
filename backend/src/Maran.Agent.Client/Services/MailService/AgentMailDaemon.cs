namespace Maran.Agent.Client.Services.MailService;

/// <summary>One of the host daemons the mail stack is made of.</summary>
/// <remarks>
/// A panel-side mirror of the wire's <c>MailDaemon</c>. The wire's UNSPECIFIED has no member here: a
/// status entry naming no daemon is refused as an invalid response and never given a name it did not
/// state.
/// </remarks>
public enum AgentMailDaemon
{
    /// <summary>The MTA: accepts and relays mail on the SMTP ports.</summary>
    Mta = 1,

    /// <summary>The IMAP, POP3, LMTP and Sieve server, which is also the login database the MTA authenticates against.</summary>
    ImapServer = 2,

    /// <summary>The spam filter, which also signs outgoing mail with DKIM and checks SPF, DKIM and DMARC on incoming mail.</summary>
    SpamFilter = 3,

    /// <summary>The Redis-protocol server holding the spam filter's per-mailbox send counters, on a unix socket.</summary>
    RateLimitStore = 4,

    /// <summary>The optional virus scanner, off unless enabling mail asked for it.</summary>
    VirusScanner = 5,
}
