using Maran.Modules.Notifications.Domain.Enums;

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
/// <b>Empty means "no password", which is a legitimate configuration.</b> A relay on localhost
/// takes no credentials at all, so the absence of a password is not an error state and
/// <see cref="Replace"/> can be told to clear one.
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

    /// <summary>The submission user name, or empty when the server takes no credentials.</summary>
    public string Username { get; private set; }

    /// <summary>
    /// The submission password, encrypted at rest and empty when the server takes none. Never
    /// returned by any query, and never written to a log or an audit entry.
    /// </summary>
    public string Password { get; private set; }

    /// <summary>The address the panel's mail is sent from.</summary>
    public string FromAddress { get; private set; }

    /// <summary>The display name beside <see cref="FromAddress"/>; may be empty.</summary>
    public string FromName { get; private set; }

    /// <summary>
    /// Where alert mail goes — the operator's own address, not a customer's.
    /// </summary>
    /// <remarks>
    /// It is part of the mail settings rather than derived from the signed-in administrator because
    /// an alert is raised by the sampler, in the background, with nobody signed in at all. There is
    /// no caller whose address could be used, and a module may not reach into Identity to look one
    /// up (rules/architecture.md "Backend: modular monolith").
    /// </remarks>
    public string AlertRecipient { get; private set; }

    /// <summary>When these settings were last saved.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Creates the panel's mail settings for the first time.</summary>
    /// <param name="host">Host name or address of the mail server.</param>
    /// <param name="port">TCP port the mail server listens on.</param>
    /// <param name="security">How the connection is protected.</param>
    /// <param name="username">The submission user name, or empty.</param>
    /// <param name="password">The submission password, or empty.</param>
    /// <param name="fromAddress">The address the panel's mail is sent from.</param>
    /// <param name="fromName">The display name beside the sender address; may be empty.</param>
    /// <param name="alertRecipient">Where alert mail goes.</param>
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
        Username = username;
        Password = password;
        FromAddress = fromAddress;
        FromName = fromName;
        AlertRecipient = alertRecipient;
        UpdatedAt = updatedAt;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private SmtpSettings()
    {
        Host = string.Empty;
        Username = string.Empty;
        Password = string.Empty;
        FromAddress = string.Empty;
        FromName = string.Empty;
        AlertRecipient = string.Empty;
    }

    /// <summary>Replaces every setting with the values just saved.</summary>
    /// <param name="host">Host name or address of the mail server.</param>
    /// <param name="port">TCP port the mail server listens on.</param>
    /// <param name="security">How the connection is protected.</param>
    /// <param name="username">The submission user name, or empty.</param>
    /// <param name="password">
    /// The new password, or <c>null</c> to keep the stored one. The empty string is NOT null here:
    /// it clears the password, which is what a move to a relay that takes no credentials needs.
    /// </param>
    /// <param name="fromAddress">The address the panel's mail is sent from.</param>
    /// <param name="fromName">The display name beside the sender address; may be empty.</param>
    /// <param name="alertRecipient">Where alert mail goes.</param>
    /// <param name="updatedAt">When the settings were saved, from the panel's clock.</param>
    /// <remarks>
    /// The <c>null</c>-keeps-the-password rule is what makes the settings screen possible at all.
    /// The form cannot show the stored password (nothing ever returns it), so it submits an empty
    /// field when the administrator did not retype one — and a save that took that literally would
    /// silently unauthenticate the panel's mail the first time anybody changed the port.
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
        Username = username;
        FromAddress = fromAddress;
        FromName = fromName;
        AlertRecipient = alertRecipient;
        UpdatedAt = updatedAt;

        if (password is not null)
        {
            Password = password;
        }
    }
}
