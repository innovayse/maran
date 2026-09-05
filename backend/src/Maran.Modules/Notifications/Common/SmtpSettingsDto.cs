using Maran.Modules.Notifications.Domain.Enums;

namespace Maran.Modules.Notifications.Common;

/// <summary>The panel's outgoing mail settings as the settings screen reads them back.</summary>
/// <remarks>
/// <para>
/// <b>There is no password field, and adding one would be the bug.</b> The stored password is a
/// credential for somebody else's mail provider; putting it in a GET response would copy it into the
/// browser's memory, every proxy log between here and there, and every screenshot of the settings
/// screen — for a value the administrator already knows and can retype (rules/security.md item 8).
/// <see cref="HasPassword"/> is what the screen actually needs: it says whether to show "a password
/// is saved" beside an empty field, which is the whole of the question the form is asking.
/// </para>
/// <para>
/// The type is what makes that guarantee structural rather than careful. There is nowhere for a
/// password to travel, so no future edit to a handler can leak one through this path.
/// </para>
/// </remarks>
/// <param name="Host">Host name or address of the mail server.</param>
/// <param name="Port">TCP port the mail server listens on.</param>
/// <param name="Security">
/// How the connection is protected. The enum itself, so the panel's one camelCase enum converter
/// decides how it is spelled. Handing out <c>ToString()</c> here made this field disagree with
/// itself across a single round trip: the read answered <c>StartTls</c> while the PUT beside it
/// bound <c>startTls</c>, so a client could not send back what it had just been given.
/// </param>
/// <param name="Username">The submission user name, or empty when the server takes no credentials.</param>
/// <param name="HasPassword">Whether a password is stored. Never the value, in any form.</param>
/// <param name="FromAddress">The address the panel's mail is sent from.</param>
/// <param name="FromName">The display name beside the sender address; may be empty.</param>
/// <param name="AlertRecipient">Where alert mail goes.</param>
/// <param name="UpdatedAt">
/// When the settings were last saved, or <c>null</c> when the panel has never had any. A null is
/// what a fresh installation reads back, alongside blank fields: the panel does not invent a
/// suggested mail server, because a plausible-looking default in a settings form is indistinguishable
/// from a value somebody configured.
/// </param>
public sealed record SmtpSettingsDto(
    string Host,
    int Port,
    SmtpSecurity Security,
    string Username,
    bool HasPassword,
    string FromAddress,
    string FromName,
    string AlertRecipient,
    DateTimeOffset? UpdatedAt);
