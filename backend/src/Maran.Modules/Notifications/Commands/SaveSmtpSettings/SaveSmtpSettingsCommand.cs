using Maran.Modules.Notifications.Domain.Enums;

namespace Maran.Modules.Notifications.Commands.SaveSmtpSettings;

/// <summary>Replaces the panel's outgoing mail settings with the ones an administrator just entered.</summary>
/// <remarks>
/// One command for the whole row rather than one per field: the settings are a single working
/// configuration, and a panel that could be left with a new host and the old port would simply stop
/// sending mail with nothing on the screen to say why.
/// </remarks>
/// <param name="Host">Host name or address of the mail server.</param>
/// <param name="Port">TCP port the mail server listens on.</param>
/// <param name="Security">How the connection is to be protected.</param>
/// <param name="Username">The submission user name, or empty when the server takes no credentials.</param>
/// <param name="Password">
/// The new password, or <c>null</c> to keep the stored one. The distinction is what makes the
/// settings form workable: the form cannot show the stored password, so it submits nothing when the
/// administrator did not retype one — and a save that read that as "clear it" would silently
/// unauthenticate the panel's mail the first time anybody changed the port. The empty string is
/// different and does clear it, which is what a move to a relay taking no credentials needs.
/// </param>
/// <param name="FromAddress">The address the panel's mail is sent from.</param>
/// <param name="FromName">The display name beside the sender address; may be empty.</param>
/// <param name="AlertRecipient">Where alert mail goes — the operator's own address.</param>
/// <param name="IpAddress">The caller's address, for the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, for the audit journal.</param>
public sealed record SaveSmtpSettingsCommand(
    string Host,
    int Port,
    SmtpSecurity Security,
    string Username,
    string? Password,
    string FromAddress,
    string FromName,
    string AlertRecipient,
    string IpAddress,
    string UserAgent);
