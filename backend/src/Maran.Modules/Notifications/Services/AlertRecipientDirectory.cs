using Maran.Sdk.Interfaces;

namespace Maran.Modules.Notifications.Services;

/// <summary>
/// This module's implementation of <see cref="IAlertRecipientDirectory"/>: the one field of the mail
/// settings another module is allowed to see.
/// </summary>
/// <remarks>
/// <para>
/// It reads through <see cref="SmtpSettingsCache"/> rather than the database, because the caller is
/// the alert evaluator running on the sampler's timer — the same background path the cache exists
/// for — and because the settings it wants are the settings a send would actually use.
/// </para>
/// <para>
/// <b>It projects one string out of a record that also holds a decrypted password, and the
/// projection is the whole point.</b> The profile never leaves this method; what crosses the module
/// boundary is an address. Returning the profile would hand every consumer of the Sdk contract a
/// working credential for the operator's mail provider (rules/security.md item 8).
/// </para>
/// <para>
/// An address that is blank or whitespace is reported as <c>null</c>: "configured to nothing" and
/// "not configured" are the same fact to a caller deciding whether it can send, and collapsing them
/// here means no caller has to remember to check both.
/// </para>
/// </remarks>
public sealed class AlertRecipientDirectory : IAlertRecipientDirectory
{
    /// <summary>The panel's mail settings, cached and invalidated on save.</summary>
    private readonly SmtpSettingsCache _settings;

    /// <summary>Creates the directory.</summary>
    /// <param name="settings">The panel's mail settings, cached and invalidated on save.</param>
    public AlertRecipientDirectory(SmtpSettingsCache settings)
    {
        _settings = settings;
    }

    /// <inheritdoc />
    public async Task<string?> GetAlertRecipientAsync(CancellationToken cancellationToken)
    {
        var profile = await _settings.GetAsync(cancellationToken);

        if (profile is null || string.IsNullOrWhiteSpace(profile.AlertRecipient))
        {
            return null;
        }

        return profile.AlertRecipient;
    }
}
