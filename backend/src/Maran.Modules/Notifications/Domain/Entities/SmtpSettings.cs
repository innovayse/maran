using Maran.Modules.Notifications.Domain.Enums;
using Maran.Modules.Notifications.Domain.ValueObjects;

namespace Maran.Modules.Notifications.Domain.Entities;

/// <summary>
/// The panel's outgoing mail configuration: at most one row, ever (R12). Without it the panel sends
/// nothing at all — no alert, no password reset — which is a state the module reports rather than
/// hides.
/// </summary>
/// <remarks>
/// <para>
/// <b>A singleton by construction, not by convention.</b> The primary key is
/// <see cref="SingletonId"/> and no code anywhere generates a different one, so a second row cannot
/// be inserted: the database refuses it. Two rows would mean two answers to "where does the panel's
/// mail go", and whichever the reader happened to load would be the one that took effect.
/// </para>
/// <para>
/// <b>The password is stored encrypted and never leaves this module in plain text.</b> The column
/// goes through <c>EncryptedStringConverter</c> (rules/csharp.md "Secret encryption at rest"), and
/// the read model exposes only whether one is set — see <c>SmtpSettingsDto.HasPassword</c>. A GET
/// that returned the value would put a provider credential into a browser, a proxy log and a
/// screenshot, for a field the administrator already knows.
/// </para>
/// <para>
/// <b>Credentials exist only in basic mode.</b> <see cref="AuthenticationMode"/> says whether the
/// panel authenticates at all, and the user name and password are <c>null</c> in the anonymous mode
/// instead of empty strings a sender has to know to test for. A relay on localhost takes no
/// credentials, so the anonymous mode is a legitimate configuration and <see cref="Replace"/> can
/// move to it.
/// </para>
/// <para>
/// <b>Expand phase.</b> The three legacy columns the previous release reads (<c>Username</c>,
/// <c>Password</c>, <c>AlertRecipient</c>) stay mapped and are rewritten from the typed state by the
/// same methods that change it, so a rollback of the application finds the columns it expects. The
/// release that follows the one that stopped reading them drops them (rules/architecture.md).
/// </para>
/// </remarks>
public sealed class SmtpSettings
{
    /// <summary>The one primary key this table ever holds.</summary>
    /// <remarks>
    /// A fixed value rather than a generated one so that "insert if missing, otherwise update" is a
    /// single primary-key lookup with no ordering, no <c>FirstOrDefault</c> over an unordered table,
    /// and no window in which two concurrent saves each create a row.
    /// </remarks>
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-00000000534d");

    /// <summary>The row's identity; always <see cref="SingletonId"/>.</summary>
    public Guid Id { get; private set; }

    /// <summary>Host name or address of the mail server.</summary>
    public string Host { get; private set; }

    /// <summary>TCP port the mail server listens on.</summary>
    public int Port { get; private set; }

    /// <summary>How the connection is protected.</summary>
    public SmtpSecurity Security { get; private set; }

    /// <summary>How the panel presents itself to the mail server.</summary>
    public SmtpAuthentication AuthenticationMode { get; private set; }

    /// <summary>The submission user name; <c>null</c> unless <see cref="AuthenticationMode"/> is basic.</summary>
    public string? BasicUsername { get; private set; }

    /// <summary>
    /// The submission password, encrypted at rest; <c>null</c> unless <see cref="AuthenticationMode"/>
    /// is basic, where it may be empty. Never returned by any query, and never written to a log or an
    /// audit entry.
    /// </summary>
    public string? BasicPassword { get; private set; }

    /// <summary>The address the panel's mail is sent from.</summary>
    public string FromAddress { get; private set; }

    /// <summary>The display name beside <see cref="FromAddress"/>; may be empty.</summary>
    public string FromName { get; private set; }

    /// <summary>
    /// Where alert mail goes — the operator's own address, not a customer's — or <c>null</c> when none
    /// is set.
    /// </summary>
    /// <remarks>
    /// It is part of the mail settings rather than derived from the signed-in administrator because
    /// an alert is raised by the sampler, in the background, with nobody signed in at all. There is
    /// no caller whose address could be used, and a module may not reach into Identity to look one
    /// up (rules/architecture.md "Backend: modular monolith").
    /// </remarks>
    public string? AlertRecipientAddress { get; private set; }

    /// <summary>When these settings were last saved.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>How the panel presents itself to the mail server, as a typed value.</summary>
    /// <remarks>
    /// The typed reading of <see cref="AuthenticationMode"/> and the two columns that exist only in
    /// basic mode, so that no sender has to remember which of them to test.
    /// </remarks>
    public SmtpCredentials Credentials
    {
        get
        {
            return AuthenticationMode == SmtpAuthentication.Basic
                ? new BasicSmtpCredentials(BasicUsername ?? string.Empty, BasicPassword ?? string.Empty)
                : new AnonymousSmtpCredentials();
        }
    }

    /// <summary>
    /// The legacy <c>Username</c> column: the user name, or empty for anonymous. Kept in step with
    /// <see cref="BasicUsername"/> by every state change and read by nothing in this release.
    /// </summary>
    /// <remarks>
    /// Contract phase: the previous release reads this column, so it stays until the release after
    /// the one that stopped reading it; then the column and this property are dropped together.
    /// </remarks>
    internal string LegacyUsername { get; private set; }

    /// <summary>
    /// The legacy <c>Password</c> column: the encrypted password, or empty for anonymous. Same
    /// contract phase and the same encrypted-string conversion as <see cref="BasicPassword"/>.
    /// </summary>
    internal string LegacyPassword { get; private set; }

    /// <summary>
    /// The legacy <c>AlertRecipient</c> column: the address, or empty when none. Same contract phase
    /// as <see cref="LegacyUsername"/>.
    /// </summary>
    internal string LegacyAlertRecipient { get; private set; }

    /// <summary>Creates the panel's mail settings for the first time.</summary>
    /// <param name="host">Host name or address of the mail server.</param>
    /// <param name="port">TCP port the mail server listens on.</param>
    /// <param name="security">How the connection is protected.</param>
    /// <param name="username">The submission user name; blank when the server takes no credentials.</param>
    /// <param name="password">The submission password; ignored when the server takes no credentials.</param>
    /// <param name="fromAddress">The address the panel's mail is sent from.</param>
    /// <param name="fromName">The display name beside the sender address; may be empty.</param>
    /// <param name="alertRecipient">Where alert mail goes; blank when no operator address is set.</param>
    /// <param name="updatedAt">When the settings were saved, from the panel's clock.</param>
    public SmtpSettings(
        string host,
        int port,
        SmtpSecurity security,
        string username,
        string password,
        string fromAddress,
        string fromName,
        string alertRecipient,
        DateTimeOffset updatedAt)
    {
        Id = SingletonId;
        Host = host;
        Port = port;
        Security = security;
        FromAddress = fromAddress;
        FromName = fromName;
        UpdatedAt = updatedAt;
        LegacyUsername = string.Empty;
        LegacyPassword = string.Empty;
        LegacyAlertRecipient = string.Empty;

        ApplyAuthentication(username, password);
        ApplyAlertRecipient(alertRecipient);
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private SmtpSettings()
    {
        Host = string.Empty;
        FromAddress = string.Empty;
        FromName = string.Empty;
        LegacyUsername = string.Empty;
        LegacyPassword = string.Empty;
        LegacyAlertRecipient = string.Empty;
    }

    /// <summary>Replaces every setting with the values just saved.</summary>
    /// <param name="host">Host name or address of the mail server.</param>
    /// <param name="port">TCP port the mail server listens on.</param>
    /// <param name="security">How the connection is protected.</param>
    /// <param name="username">The submission user name; blank moves the settings to anonymous mode.</param>
    /// <param name="password">
    /// The new password, or <c>null</c> to keep the stored one. The empty string is NOT null here:
    /// it is a password of no characters. Ignored when <paramref name="username"/> is blank.
    /// </param>
    /// <param name="fromAddress">The address the panel's mail is sent from.</param>
    /// <param name="fromName">The display name beside the sender address; may be empty.</param>
    /// <param name="alertRecipient">Where alert mail goes; blank clears the operator address.</param>
    /// <param name="updatedAt">When the settings were saved, from the panel's clock.</param>
    /// <remarks>
    /// The <c>null</c>-keeps-the-password rule is what makes the settings screen possible at all.
    /// The form cannot show the stored password (nothing ever returns it), so it submits no password
    /// when the administrator did not retype one — and a save that took that literally would
    /// silently change the panel's mail credentials the first time anybody changed the port. The
    /// blank-means-none reading of <paramref name="username"/> and <paramref name="alertRecipient"/>
    /// is the spelling of the settings form's request and is applied here, once, on the way in.
    /// </remarks>
    public void Replace(
        string host,
        int port,
        SmtpSecurity security,
        string username,
        string? password,
        string fromAddress,
        string fromName,
        string alertRecipient,
        DateTimeOffset updatedAt)
    {
        Host = host;
        Port = port;
        Security = security;
        FromAddress = fromAddress;
        FromName = fromName;
        UpdatedAt = updatedAt;

        ApplyAuthentication(username, password ?? BasicPassword);
        ApplyAlertRecipient(alertRecipient);
    }

    /// <summary>Sets the authentication mode and the credentials that exist only in basic mode.</summary>
    /// <param name="username">The submission user name; blank selects the anonymous mode.</param>
    /// <param name="password">The password to store in basic mode; <c>null</c> means an empty one.</param>
    private void ApplyAuthentication(string username, string? password)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            AuthenticationMode = SmtpAuthentication.Anonymous;
            BasicUsername = null;
            BasicPassword = null;
        }
        else
        {
            AuthenticationMode = SmtpAuthentication.Basic;
            BasicUsername = username;
            BasicPassword = password ?? string.Empty;
        }

        LegacyUsername = BasicUsername ?? string.Empty;
        LegacyPassword = BasicPassword ?? string.Empty;
    }

    /// <summary>Sets the operator's alert address.</summary>
    /// <param name="alertRecipient">The address; blank clears it.</param>
    private void ApplyAlertRecipient(string alertRecipient)
    {
        AlertRecipientAddress = string.IsNullOrWhiteSpace(alertRecipient) ? null : alertRecipient;
        LegacyAlertRecipient = AlertRecipientAddress ?? string.Empty;
    }
}
