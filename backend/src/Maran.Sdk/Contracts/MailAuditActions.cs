namespace Maran.Sdk.Contracts;

/// <summary>
/// The machine-stable audit action names for outgoing mail settings and delivery actions. Constants rather than an enum: a marketplace
/// module records actions this assembly was never compiled knowing about, and an enum could not be
/// extended from outside.
/// </summary>
/// <remarks>
/// One of the Sdk's <c>*AuditActions</c> classes, which together are the closed set of actions the
/// shipped modules record; each is named for the area whose journal writes its constants.
/// </remarks>
public static class MailAuditActions
{
    /// <summary>The panel's outgoing mail settings were saved. The password is never part of the entry.</summary>
    public const string SmtpSettingsSaved = "SmtpSettingsSaved";

    /// <summary>A test message was sent to the administrator who asked for it.</summary>
    public const string TestMailSent = "TestMailSent";

    /// <summary>
    /// Mail was wanted and no SMTP settings exist, so nothing was sent. Recorded because the
    /// alternative is a password reset that silently never arrives.
    /// </summary>
    public const string MailSkippedNoSmtp = "MailSkippedNoSmtp";

    /// <summary>The mail server refused or could not be reached. Recorded with the reason, never with the body.</summary>
    public const string MailSendFailed = "MailSendFailed";
}
