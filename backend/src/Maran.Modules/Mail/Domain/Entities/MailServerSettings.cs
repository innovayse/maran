namespace Maran.Modules.Mail.Domain.Entities;

/// <summary>
/// What an administrator asked of this server's mail stack: whether it is on, the name it presents and
/// the few limits the daemons are configured with. At most one row exists.
/// </summary>
/// <remarks>
/// <para>
/// This row is an INTENTION. What the daemons actually do is read back from the host on every status
/// call and never stored, so a status screen cannot agree with the panel by construction. The row
/// carries no <c>AccountId</c>: it belongs to the server and to no customer, and what authorises every
/// path to it is the administrator-only policy on the server controller.
/// </para>
/// <para>
/// Nothing about the webmail's master secret is kept here. It is generated and held outside the
/// database (rules/security.md item 8) and reaches the host through the enable call.
/// </para>
/// </remarks>
public sealed class MailServerSettings
{
    /// <summary>The fixed identity of the one row, so a second row is a key violation and not a second opinion.</summary>
    public static readonly Guid SingletonId = new("3f5b2a1c-8e47-4d36-9b0a-6c1d7e2f4a10");

    /// <summary>The row's identity; always <see cref="SingletonId"/>.</summary>
    public Guid Id { get; private set; }

    /// <summary>Whether an administrator wants the mail daemons running.</summary>
    public bool Enabled { get; private set; }

    /// <summary>The fully qualified name the server presents in its greeting and certificate, and the MX targets.</summary>
    public string Hostname { get; private set; }

    /// <summary>Whether the POP3 service runs beside IMAP.</summary>
    public bool Pop3Enabled { get; private set; }

    /// <summary>Whether the optional virus scanner runs.</summary>
    public bool AntivirusEnabled { get; private set; }

    /// <summary>Whether a forwarder may deliver to a domain this account does not host.</summary>
    public bool AllowExternalForwarding { get; private set; }

    /// <summary>The largest message the MTA accepts, in MiB.</summary>
    public int MaxMessageMib { get; private set; }

    /// <summary>The spam score at which a message is greylisted.</summary>
    public int SpamGreylistScore { get; private set; }

    /// <summary>The spam score at which a message is marked as spam.</summary>
    public int SpamAddHeaderScore { get; private set; }

    /// <summary>The spam score at which a message is refused.</summary>
    public int SpamRejectScore { get; private set; }

    /// <summary>The instant of the last change.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Records the first time an administrator turns mail on.</summary>
    /// <param name="hostname">The name the server presents.</param>
    /// <param name="pop3Enabled">Whether POP3 runs.</param>
    /// <param name="antivirusEnabled">Whether the virus scanner runs.</param>
    /// <param name="allowExternalForwarding">Whether forwarders may leave the account's domains.</param>
    /// <param name="maxMessageMib">The largest message, in MiB.</param>
    /// <param name="spamGreylistScore">The greylist score.</param>
    /// <param name="spamAddHeaderScore">The mark-as-spam score.</param>
    /// <param name="spamRejectScore">The refuse score.</param>
    /// <param name="at">The instant, taken from <see cref="IClock"/>.</param>
    public MailServerSettings(
        string hostname,
        bool pop3Enabled,
        bool antivirusEnabled,
        bool allowExternalForwarding,
        int maxMessageMib,
        int spamGreylistScore,
        int spamAddHeaderScore,
        int spamRejectScore,
        DateTimeOffset at)
    {
        Id = SingletonId;
        Enabled = true;
        Hostname = hostname;
        Pop3Enabled = pop3Enabled;
        AntivirusEnabled = antivirusEnabled;
        AllowExternalForwarding = allowExternalForwarding;
        MaxMessageMib = maxMessageMib;
        SpamGreylistScore = spamGreylistScore;
        SpamAddHeaderScore = spamAddHeaderScore;
        SpamRejectScore = spamRejectScore;
        UpdatedAt = at;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private MailServerSettings()
    {
        Hostname = string.Empty;
    }

    /// <summary>Turns mail on again, or re-states the settings of a stack that is already on.</summary>
    /// <param name="hostname">The name the server presents.</param>
    /// <param name="pop3Enabled">Whether POP3 runs.</param>
    /// <param name="antivirusEnabled">Whether the virus scanner runs.</param>
    /// <param name="allowExternalForwarding">Whether forwarders may leave the account's domains.</param>
    /// <param name="maxMessageMib">The largest message, in MiB.</param>
    /// <param name="spamGreylistScore">The greylist score.</param>
    /// <param name="spamAddHeaderScore">The mark-as-spam score.</param>
    /// <param name="spamRejectScore">The refuse score.</param>
    /// <param name="at">The instant, taken from <see cref="IClock"/>.</param>
    public void Enable(
        string hostname,
        bool pop3Enabled,
        bool antivirusEnabled,
        bool allowExternalForwarding,
        int maxMessageMib,
        int spamGreylistScore,
        int spamAddHeaderScore,
        int spamRejectScore,
        DateTimeOffset at)
    {
        Enabled = true;
        Hostname = hostname;
        Pop3Enabled = pop3Enabled;
        AntivirusEnabled = antivirusEnabled;
        AllowExternalForwarding = allowExternalForwarding;
        MaxMessageMib = maxMessageMib;
        SpamGreylistScore = spamGreylistScore;
        SpamAddHeaderScore = spamAddHeaderScore;
        SpamRejectScore = spamRejectScore;
        UpdatedAt = at;
    }

    /// <summary>Records that an administrator turned the daemons off. Every setting is kept for the next enable.</summary>
    /// <param name="at">The instant, taken from <see cref="IClock"/>.</param>
    public void Disable(DateTimeOffset at)
    {
        Enabled = false;
        UpdatedAt = at;
    }
}
