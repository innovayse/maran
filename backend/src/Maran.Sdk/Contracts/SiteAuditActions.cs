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

    /// <summary>A PHP version was installed on this server.</summary>
    /// <remarks>
    /// Server-wide rather than per-account, and that is why it is journalled separately from
    /// <see cref="SitePhpVersionChanged"/>: changing one site's version affects one customer,
    /// while installing a runtime changes what every site on the box may be pointed at, and adds
    /// packages an administrator will later have to patch. The subject is the version, because
    /// that is the only thing the operation acts on.
    /// </remarks>
    public const string PhpVersionInstalled = "PhpVersionInstalled";

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
}
