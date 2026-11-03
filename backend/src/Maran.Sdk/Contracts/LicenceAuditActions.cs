namespace Maran.Sdk.Contracts;

/// <summary>
/// The machine-stable audit action names for licence status and installation actions. Constants rather than an enum: a marketplace
/// module records actions this assembly was never compiled knowing about, and an enum could not be
/// extended from outside.
/// </summary>
/// <remarks>
/// One of the Sdk's <c>*AuditActions</c> classes, which together are the closed set of actions the
/// shipped modules record; each is named for the area whose journal writes its constants.
/// </remarks>
public static class LicenceAuditActions
{
    /// <summary>
    /// An administrator read the installation's licence status. Recorded on every read, never only on
    /// a refusal: a licence status names the product, the tier and the expiry of the whole
    /// installation, and who looked at that fact and when is worth a date and an author the same way
    /// <see cref="SiteAuditActions.SiteLogTailed"/> already is for a read of a different sensitive surface. The subject
    /// carries which of the three states was observed and, for a valid licence, its id and tier — never
    /// its signature and never the fingerprint inputs a future slice's §228 work would add
    /// (docs/superpowers/notes/2026-09-22-licence-verification-threat-note.md §4).
    /// </summary>
    public const string LicenceStatusRead = "LicenceStatusRead";

    /// <summary>
    /// An administrator attempted to install or replace the installation's licence artefact.
    /// Recorded on EVERY outcome, success and refusal alike — the same argument
    /// <see cref="LicenceStatusRead"/> already makes for a mere read, extended here to a write that
    /// gates paid modules for the whole installation. The subject carries the outcome and, on
    /// success, the new licence's id/tier and the PREVIOUS licence's id when one existed — never the
    /// uploaded bytes, the Ed25519 signature, or any fingerprint input
    /// (docs/superpowers/notes/2026-09-22-licence-installation-threat-note.md §5).
    /// </summary>
    public const string LicenceInstalled = "LicenceInstalled";
}
