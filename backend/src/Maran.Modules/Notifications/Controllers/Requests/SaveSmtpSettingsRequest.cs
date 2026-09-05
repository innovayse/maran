using Maran.Modules.Notifications.Domain.Enums;

namespace Maran.Modules.Notifications.Controllers.Requests;

/// <summary>The body of a request to save the panel's outgoing mail settings.</summary>
/// <remarks>
/// <see cref="Password"/> is nullable and its absence is meaningful: the settings screen cannot show
/// the stored password — nothing ever returns it — so it omits the field when the administrator did
/// not retype one, and the save keeps what is stored. Sending an empty string is a different
/// instruction and clears it.
/// </remarks>
/// <param name="Host">Host name or address of the mail server.</param>
/// <param name="Port">TCP port the mail server listens on.</param>
/// <param name="Security">How the connection is to be protected.</param>
/// <param name="Username">The submission user name, or empty when the server takes no credentials.</param>
/// <param name="Password">The new password, or absent to keep the stored one.</param>
/// <param name="FromAddress">The address the panel's mail is sent from.</param>
/// <param name="FromName">The display name beside the sender address; may be empty.</param>
/// <param name="AlertRecipient">Where alert mail goes.</param>
public sealed record SaveSmtpSettingsRequest(
    string Host,
    int Port,
    SmtpSecurity Security,
    string Username,
    string? Password,
    string FromAddress,
    string FromName,
    string AlertRecipient);
