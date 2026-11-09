namespace Maran.Sdk.Contracts;

/// <summary>
/// The machine-stable audit action names for site, log-tail and PHP runtime actions. Constants rather than an enum: a marketplace
/// module records actions this assembly was never compiled knowing about, and an enum could not be
/// extended from outside.
/// </summary>
/// <remarks>
/// One of the Sdk's <c>*AuditActions</c> classes, which together are the closed set of actions the
/// shipped modules record; each is named for the area whose journal writes its constants.
/// </remarks>
public static class SiteAuditActions
{
    /// <summary>A site was created: its document root, vhost and pool now exist on the host.</summary>
    public const string SiteCreated = "SiteCreated";

    /// <summary>A site was rebound to a different installed PHP version.</summary>
    public const string SitePhpVersionChanged = "SitePhpVersionChanged";

    /// <summary>
    /// A site's per-site options (redirects, basic authentication, IP rules, upload size, cache headers)
    /// were replaced.
    /// </summary>
    /// <remarks>
    /// The subject names the site's domain, the option groups that changed and the basic-authentication
    /// user NAMES; never a password, a hash or an address list's contents.
    /// </remarks>
    public const string SiteOptionsChanged = "SiteOptionsChanged";

    /// <summary>A PHP version was installed on this server.</summary>
    /// <remarks>
    /// Server-wide rather than per-account, and that is why it is journalled separately from
    /// <see cref="SitePhpVersionChanged"/>: changing one site's version affects one customer,
    /// while installing a runtime changes what every site on the box may be pointed at, and adds
    /// packages an administrator will later have to patch. The subject is the version, because
    /// that is the only thing the operation acts on.
    /// </remarks>
    public const string PhpVersionInstalled = "PhpVersionInstalled";

    /// <summary>A PHP extension was switched on or off for one PHP version, host-wide (issue #157).</summary>
    /// <remarks>
    /// Recorded on the refusals and failures too. Server-wide like <see cref="PhpVersionInstalled"/>:
    /// the change reaches every account's pool on that version at the reload. The subject is the
    /// version, the catalogue name and the requested state, e.g. <c>8.3: gd enabled</c>; it names no
    /// package and none of the agent's output.
    /// </remarks>
    public const string PhpExtensionChanged = "PhpExtensionChanged";

    /// <summary>A site was returned to normal serving.</summary>
    public const string SiteEnabled = "SiteEnabled";

    /// <summary>A site was made to serve a suspension response instead of its content.</summary>
    public const string SiteDisabled = "SiteDisabled";

    /// <summary>A site was removed: its vhost is gone, its files are left alone.</summary>
    public const string SiteDeleted = "SiteDeleted";

    /// <summary>
    /// A site's log was opened for tailing. Recorded on the refusals too: a request for a log the
    /// caller may not read is a probe, and the journal is where a pattern of them becomes visible.
    /// </summary>
    public const string SiteLogTailed = "SiteLogTailed";

    /// <summary>
    /// A customer's PHP settings were replaced (issue #138). The subject is the account's system user
    /// name; the settings' names that changed travel with the entry.
    /// </summary>
    public const string PhpSettingsChanged = "PhpSettingsChanged";

    /// <summary>
    /// A site's log retention was changed (issue #152). The subject is the site's domain; the archives
    /// themselves are removed by the next maintenance pass, which records <see cref="SiteLogsPruned"/>.
    /// </summary>
    public const string SiteLogRetentionChanged = "SiteLogRetentionChanged";

    /// <summary>
    /// The panel removed a site's rotated log archives beyond its retention (issue #152). Recorded only
    /// when files were actually removed, under the panel's own actor, because nobody asked for it that hour.
    /// </summary>
    public const string SiteLogsPruned = "SiteLogsPruned";
}
