namespace Maran.Modules.Mail.Common;

/// <summary>
/// The values a mail server is turned on with when an administrator names none, produced by the backend so the
/// interface holds no figure of its own.
/// </summary>
/// <param name="Pop3Enabled">Whether POP3 and POP3S are served beside IMAP.</param>
/// <param name="AntivirusEnabled">Whether incoming mail is scanned for viruses.</param>
/// <param name="AllowExternalForwarding">Whether a forwarder may point outside its account's own mail domains.</param>
/// <param name="MaxMessageMib">The largest message accepted, in mebibytes.</param>
/// <param name="SpamGreylistScore">The spam score from which a message is greylisted.</param>
/// <param name="SpamAddHeaderScore">The spam score from which a spam header is added.</param>
/// <param name="SpamRejectScore">The spam score from which a message is rejected.</param>
public sealed record MailServerStartingValuesDto(
    bool Pop3Enabled,
    bool AntivirusEnabled,
    bool AllowExternalForwarding,
    int MaxMessageMib,
    int SpamGreylistScore,
    int SpamAddHeaderScore,
    int SpamRejectScore);
