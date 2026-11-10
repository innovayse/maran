namespace Maran.Sdk.Contracts;

/// <summary>
/// The machine-stable audit action names for the panel's own update when it is started from the panel
/// (issue #382). Constants rather than an enum: a marketplace module records actions this assembly was
/// never compiled knowing about, and an enum could not be extended from outside.
/// </summary>
/// <remarks>
/// One of the Sdk's <c>*AuditActions</c> classes, which together are the closed set of actions the
/// shipped modules record; each is named for the area whose journal writes its constants. The
/// unattended security-patch update has its own constants in <see cref="MonitoringAuditActions"/>;
/// these are for the run an administrator asked for.
/// </remarks>
public static class UpdatesAuditActions
{
    /// <summary>
    /// An administrator asked the panel to update itself. Written BEFORE the request marker is
    /// created, so a started update can never be unaudited; a request that could not be handed to the
    /// root update service is journalled again as a failed entry. The subject is the fixed
    /// <c>request=update</c>: the request carries nothing else.
    /// </summary>
    public const string PanelUpdateRequested = "PanelUpdateRequested";

    /// <summary>
    /// An administrator asked the panel to check for updates (issue #516). Written BEFORE the check
    /// marker is created, like <see cref="PanelUpdateRequested"/>; a request that could not be handed
    /// to the root check service is journalled again as a failed entry. The subject is the fixed
    /// <c>request=check</c>: the request carries nothing else.
    /// </summary>
    public const string PanelUpdateCheckRequested = "PanelUpdateCheckRequested";

    /// <summary>The requested update installed a newer release and its health check passed. Subject: <c>from=..;to=..</c>.</summary>
    public const string PanelUpdateRunApplied = "PanelUpdateRunApplied";

    /// <summary>The requested update found the panel already on the newest release and installed nothing.</summary>
    public const string PanelUpdateRunUnchanged = "PanelUpdateRunUnchanged";

    /// <summary>The root update refused the requested update before it changed anything (signature, space, lock).</summary>
    public const string PanelUpdateRunRefused = "PanelUpdateRunRefused";

    /// <summary>The requested update failed its health check and the previous release was restored.</summary>
    public const string PanelUpdateRunRolledBack = "PanelUpdateRunRolledBack";

    /// <summary>The requested update failed and the previous release did not become healthy either.</summary>
    public const string PanelUpdateRunRollbackFailed = "PanelUpdateRunRollbackFailed";

    /// <summary>The requested update was interrupted after the release was switched.</summary>
    public const string PanelUpdateRunInterrupted = "PanelUpdateRunInterrupted";

    /// <summary>The root update service never started the requested update, and the request was withdrawn.</summary>
    public const string PanelUpdateRunNotStarted = "PanelUpdateRunNotStarted";

    /// <summary>No report of the requested update's outcome arrived, so the task was closed without one.</summary>
    public const string PanelUpdateRunOutcomeUnknown = "PanelUpdateRunOutcomeUnknown";
}
