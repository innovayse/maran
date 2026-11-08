namespace Maran.Modules.Mail.Common;

/// <summary>What an administrator asked the mail server to be: the panel's intention, not the host's state.</summary>
/// <param name="Enabled">Whether the administrator wants the mail daemons running.</param>
/// <param name="Hostname">The host name the server presents.</param>
/// <param name="Pop3Enabled">Whether POP3 and POP3S are served beside IMAP.</param>
/// <param name="AntivirusEnabled">Whether incoming mail is scanned for viruses.</param>
/// <param name="AllowExternalForwarding">Whether a forwarder may point outside its account's own mail domains.</param>
/// <param name="MaxMessageMib">The largest message accepted, in mebibytes.</param>
/// <param name="SpamGreylistScore">The spam score from which a message is greylisted.</param>
/// <param name="SpamAddHeaderScore">The spam score from which a spam header is added.</param>
/// <param name="SpamRejectScore">The spam score from which a message is rejected.</param>
/// <param name="UpdatedAt">When the settings were last changed.</param>
public sealed record MailServerSettingsDto(
    bool Enabled,
    string Hostname,
    bool Pop3Enabled,
    bool AntivirusEnabled,
    bool AllowExternalForwarding,
    int MaxMessageMib,
    int SpamGreylistScore,
    int SpamAddHeaderScore,
    int SpamRejectScore,
    DateTimeOffset UpdatedAt);
