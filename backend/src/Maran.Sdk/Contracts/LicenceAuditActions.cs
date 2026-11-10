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

    /// <summary>
    /// A module the installed licence covered stopped being covered because its cover ran out: the licence
    /// expired or its lease ended. Written by the panel itself when it OBSERVES the change (the watcher
    /// polls the licence window every few minutes), so the time on the entry is the time the panel noticed,
    /// not the instant the cover ended. The subject is <c>module=&lt;id&gt;;from=&lt;verdict&gt;;to=Lapsed</c>,
    /// where <c>from</c> is <c>Unobserved</c> for the first reading after the panel started; it never
    /// carries the licence id, text, signature or machine id
    /// (docs/superpowers/notes/2026-11-07-licensing-lease-threat-note.md).
    /// </summary>
    public const string ModuleLapsed = "LicenceModuleLapsed";

    /// <summary>
    /// A module that had lapsed is covered again: a licence or lease that covers it was installed. Written
    /// by the panel itself on observing the change; the subject is
    /// <c>module=&lt;id&gt;;from=Lapsed;to=Covered</c>.
    /// </summary>
    public const string ModuleRecovered = "LicenceModuleRecovered";

    /// <summary>
    /// The panel fetched a new licence lease from the licence service, verified it with the pinned lease key and
    /// stored it. Written by the panel itself (actor <c>maran-licensing</c>, no account) once per renewal. The
    /// subject is <c>outcome=Renewed;validUntil=&lt;ISO instant&gt;</c>: the end of the new lease and nothing else,
    /// never the licence id, the lease text, the signature or the machine id
    /// (docs/superpowers/notes/2026-11-07-licensing-lease-threat-note.md).
    /// </summary>
    public const string LeaseRenewed = "LicenceLeaseRenewed";

    /// <summary>
    /// A renewal of the licence lease did not produce a stored lease: the service could not be reached or
    /// refused, its answer was not a lease this licence could use, or the file could not be written. The old
    /// lease, if any, is untouched. Written by the panel itself, once per failed attempt; the subject is
    /// <c>outcome=&lt;word&gt;;consecutiveFailures=&lt;n&gt;</c>.
    /// </summary>
    public const string LeaseRenewalFailed = "LicenceLeaseRenewalFailed";
}
