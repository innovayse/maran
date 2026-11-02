using Maran.Agent.Client.Services.AccountsService;
using Maran.Agent.Client.Services.MonitorService;
using Maran.Modules.Monitoring.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Monitoring.Mappers;

/// <summary>
/// Renders the figures one round observed into the invariant text an alert carries: the detail in
/// the mail body, and the <c>key=value</c> subject the journal records for a drifted code-integrity
/// report.
/// </summary>
/// <remarks>
/// Pure functions from values to text, split from <see cref="AlertObserver"/> so the observation
/// logic reads as observations. The words around each figure come from the resx in the reader's
/// language; only the figure itself is rendered here.
/// </remarks>
public static class AlertDetailMapper
{
    /// <summary>How many differing paths a code-integrity alert's mail body names before it switches to a count.</summary>
    /// <remarks>
    /// An operator reading a mail about a handful of files needs the files; an operator reading a
    /// mail about thousands needs a count and "see the panel", not a wall of text — the same
    /// order-of-magnitude reasoning docs/superpowers/plans/2026-09-19-maran-code-integrity.md Task 3
    /// applies to the mail body's cap.
    /// </remarks>
    public const int DifferingPathsMailCap = 50;

    /// <summary>How many differing paths a code-integrity alert's audit subject names, per the journal's own "count, never everything" doctrine.</summary>
    /// <remarks>
    /// Shorter than <see cref="DifferingPathsMailCap"/> on purpose: the journal is not the detailed
    /// report, matching
    /// <c>RepairDatabaseGrantsCommandHandler</c>'s own precedent that a journal subject is a count,
    /// never a dump. Unlike that precedent, naming a few of the paths here leaks nothing — every path
    /// is the panel's own code, not another tenant's data — so a short bounded list is included
    /// alongside the count rather than the count alone.
    /// </remarks>
    public const int DifferingPathsAuditCap = 10;

    /// <summary>Renders the differing paths of a drifted code-integrity report for the mail body.</summary>
    /// <param name="differingPaths">The paths the closed PluginLoader reported as not matching.</param>
    /// <returns>
    /// Every path, comma-joined, up to <see cref="DifferingPathsMailCap"/>; beyond that, the first
    /// entries plus a count of how many more were not shown — an operator reading a mail about
    /// thousands of files needs a count and "see the panel", never a wall of text.
    /// </returns>
    public static string FormatDifferingPathsBody(IReadOnlyList<string> differingPaths)
    {
        if (differingPaths.Count <= DifferingPathsMailCap)
        {
            return string.Join(", ", differingPaths);
        }

        var shown = differingPaths.Take(DifferingPathsMailCap);
        return string.Join(", ", shown) + $", and {differingPaths.Count - DifferingPathsMailCap} more";
    }

    /// <summary>Renders the journal's <c>key=value</c> subject for a drifted code-integrity report.</summary>
    /// <param name="report">The report to summarize.</param>
    /// <param name="subject">The plain alert subject (<see cref="AlertEvaluator.InstalledFilesSubject"/>).</param>
    /// <returns>
    /// <c>installed-files;version=&lt;version&gt;;count=&lt;n&gt;;paths=&lt;a,b,...&gt;</c> — the
    /// version and the count are never truncated (the count is the figure that matters most), the
    /// path list is capped at <see cref="DifferingPathsAuditCap"/>, following
    /// <c>RepairDatabaseGrantsCommandHandler</c>'s "the subject is a count, never everything"
    /// doctrine, in the <c>key=value;key=value</c> shape that same handler established so the
    /// journal reads the same regardless of the panel's active locale.
    /// </returns>
    public static string FormatDifferingPathsAuditSubject(CodeIntegrityReport report, string subject)
    {
        var capped = report.DifferingPaths.Take(DifferingPathsAuditCap);
        return $"{subject};version={report.InstalledVersion};count={report.DifferingPaths.Count};paths={string.Join(",", capped)}";
    }

    /// <summary>Renders a percentage for the body of an alert mail.</summary>
    /// <param name="percent">The percentage to render.</param>
    /// <returns>The value with one decimal place, in the invariant culture.</returns>
    /// <remarks>
    /// Invariant rather than the current culture, because this is a number inside a sentence that is
    /// itself localized: the sentence comes from the resx in the reader's language, while the figure
    /// has one unambiguous spelling. A background sender has no request culture to read anyway.
    /// </remarks>
    public static string FormatPercent(double percent)
    {
        return percent.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Renders a byte count as gibibytes with one decimal, for an alert mail's body.</summary>
    /// <param name="bytes">The figure the agent reported.</param>
    /// <returns>The figure in GiB, formatted invariantly, as <see cref="FormatPercent"/> is.</returns>
    public static string FormatGibibytes(ulong bytes)
    {
        return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Renders what the agent found missing, for the body of a jail-drift alert mail.</summary>
    /// <param name="missing">
    /// What the agent's block check did not find — the installer's own words, empty when the block
    /// is intact.
    /// </param>
    /// <returns>A comma-separated list, or a fixed sentence when nothing is missing.</returns>
    /// <remarks>
    /// The resolved mail does not need this detail — it says the block is back — but the same
    /// pending row is built for both directions, so this must answer sensibly for an empty list
    /// too rather than being read only on the raising path.
    /// </remarks>
    public static string FormatMissing(IReadOnlyList<string> missing)
    {
        return missing.Count == 0 ? string.Empty : string.Join(", ", missing);
    }

    /// <summary>Renders why the filesystem cannot enforce a quota, for the body of the alert mail.</summary>
    /// <param name="reason">The agent's own classification.</param>
    /// <returns>A short, invariant phrase naming the reason.</returns>
    /// <remarks>
    /// The resolved mail does not read this — it says enforcement is back — but the same pending row
    /// is built for both directions, so this must answer sensibly for
    /// <see cref="QuotaUnenforceableReason.Unspecified"/> too rather than being read only on the
    /// raising path.
    /// </remarks>
    public static string FormatReason(QuotaUnenforceableReason reason)
    {
        return reason switch
        {
            QuotaUnenforceableReason.MountedWithoutQuotaAccounting => "not mounted with quota accounting",
            QuotaUnenforceableReason.AccountingNotEnabled => "quota accounting is off",
            _ => "unknown",
        };
    }

    /// <summary>Describes the host clock's synchronisation for the alert mail's body.</summary>
    /// <param name="state">What the agent reported.</param>
    /// <returns>A short, invariant phrase, as <see cref="FormatReason"/> returns.</returns>
    public static string FormatClockState(AgentClockSyncState state)
    {
        return state switch
        {
            AgentClockSyncState.Synchronized => "synchronised",
            AgentClockSyncState.NotSynchronized =>
                "a synchronisation service is enabled but has not synchronised the clock",
            AgentClockSyncState.SyncServiceDisabled => "no synchronisation service is enabled",
            _ => "unknown",
        };
    }
}
