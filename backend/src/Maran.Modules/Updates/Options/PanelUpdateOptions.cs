using System.ComponentModel.DataAnnotations;

namespace Maran.Modules.Updates.Options;

/// <summary>
/// Where the panel finds the facts the root update writes and the places it hands a request to. A server's
/// own values are never set: the defaults are the paths the installer builds. The seam exists so a test can
/// point every path at an isolated directory (issue #382).
/// </summary>
/// <remarks>
/// Every path except <see cref="TaskLedgerPath"/> and the request marker belongs to root: the panel only
/// reads them, and treats what it reads as untrusted text (rules/security.md item 1).
/// </remarks>
public sealed class PanelUpdateOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "PanelUpdate";

    /// <summary>The root-owned release record: channel, running version, previous version.</summary>
    [Required]
    public string ReleaseStatePath { get; set; } = "/etc/maran/release.state";

    /// <summary>The root-owned record of the last signed manifest the root CLI fetched.</summary>
    [Required]
    public string CheckStatePath { get; set; } = "/etc/maran/update-check.state";

    /// <summary>The root-owned record that exists while an update verb holds the lock.</summary>
    [Required]
    public string RunningStatePath { get; set; } = "/etc/maran/update-running.state";

    /// <summary>The directory holding one directory per installed release.</summary>
    [Required]
    public string ReleasesDirectory { get; set; } = "/usr/local/maran/releases";

    /// <summary>The root-owned directory the panel may create its request marker in.</summary>
    [Required]
    public string RequestDirectory { get; set; } = "/run/maran-update-request";

    /// <summary>The marker's file name inside <see cref="RequestDirectory"/>.</summary>
    [Required]
    public string RequestMarkerName { get; set; } = "requested";

    /// <summary>The check request's marker file name inside <see cref="RequestDirectory"/>: the panel's "Check for updates" asks root for a release check by creating it (issue #516).</summary>
    [Required]
    public string CheckRequestMarkerName { get; set; } = "check-requested";

    /// <summary>The panel's own file naming the task an update request opened.</summary>
    [Required]
    public string TaskLedgerPath { get; set; } = "/var/lib/maran/panel-update-task";

    /// <summary>The full path of the check request marker: <see cref="CheckRequestMarkerName"/> inside <see cref="RequestDirectory"/>.</summary>
    /// <remarks>Derived, like <see cref="MarkerPath"/>.</remarks>
    public string CheckMarkerPath
    {
        get
        {
            return Path.Combine(RequestDirectory, CheckRequestMarkerName);
        }
    }

    /// <summary>The full path of the request marker: <see cref="RequestMarkerName"/> inside <see cref="RequestDirectory"/>.</summary>
    /// <remarks>Derived, so it is not bound from configuration and the two parts can never disagree with it.</remarks>
    public string MarkerPath
    {
        get
        {
            return Path.Combine(RequestDirectory, RequestMarkerName);
        }
    }
}
