namespace Maran.Modules.Licensing.Options;

/// <summary>
/// How early the panel warns that the installed licence is about to expire. The backend owns the thresholds; the
/// screen, <c>maran status</c> and the notification all read the same ones. Bound from the same <c>Licensing</c>
/// section as the file paths and the lease service address.
/// </summary>
public sealed class LicenceExpiryWarningOptions
{
    /// <summary>The configuration section the options bind from; shared with the licence and lease paths.</summary>
    public const string SectionName = "Licensing";

    /// <summary>
    /// The number of days before the expiry at which a warning begins, each one a further step the notification
    /// is repeated at. Null means the documented defaults (<c>LicenceExpiryWarningPolicy.DefaultThresholdDays</c>);
    /// an empty list means no warning at all.
    /// Every value must be between 1 and <c>LicenceExpiryWarningPolicy.MaxThresholdDays</c>; a value outside that
    /// range stops the panel starting, because a threshold that silently never fires is worse than no option.
    /// </summary>
    public int[]? ExpiryWarningDays { get; set; }
}
