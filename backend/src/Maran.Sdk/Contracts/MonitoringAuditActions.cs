namespace Maran.Sdk.Contracts;

/// <summary>
/// The machine-stable audit action names for alert, runtime log level and code-integrity actions. Constants rather than an enum: a marketplace
/// module records actions this assembly was never compiled knowing about, and an enum could not be
/// extended from outside.
/// </summary>
/// <remarks>
/// One of the Sdk's <c>*AuditActions</c> classes, which together are the closed set of actions the
/// shipped modules record; each is named for the area whose journal writes its constants.
/// </remarks>
public static class MonitoringAuditActions
{
    /// <summary>
    /// An administrator changed the panel's log level at runtime. The subject names the level before
    /// and after, so the journal answers "why did the logs go quiet" on its own (issue #131).
    /// </summary>
    public const string PanelLogLevelChanged = "PanelLogLevelChanged";

    /// <summary>
    /// A monitored condition crossed into alarm — a full disk, a stopped service, a drifted SFTP
    /// jail configuration block. Generic across every <c>AlertKind</c> on purpose: the journalled
    /// subject already names which condition and which thing of that kind, so a new kind of alert
    /// needs no action constant of its own.
    /// </summary>
    public const string AlertRaised = "AlertRaised";

    /// <summary>A monitored condition returned to normal. Paired with the raise so an operator can read the outage's length.</summary>
    public const string AlertResolved = "AlertResolved";

    /// <summary>
    /// The closed PluginLoader handed <c>ICodeIntegrityReportSink.ReportAsync</c> a
    /// <c>CodeIntegrityReport</c> whose <c>Outcome</c> was <c>Drifted</c> with an empty
    /// <c>DifferingPaths</c> — internally inconsistent, since a drift with nothing named to have
    /// drifted is not a report a correct comparison can produce. Recorded rather than silently
    /// treated as <c>Clean</c> (docs/superpowers/plans/2026-09-19-maran-code-integrity.md Task 3's
    /// vacuity trap), and rather than treated as <c>Unavailable</c>: the malformed report is the
    /// closed side's own defect, not evidence the hash list could not be read.
    /// </summary>
    public const string CodeIntegrityReportRejected = "CodeIntegrityReportRejected";
}
