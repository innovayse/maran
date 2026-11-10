using Maran.Modules.Updates.Domain.Enums;
using Maran.Modules.Updates.Domain.ValueObjects;

namespace Maran.Modules.Updates.Domain.Policies;

/// <summary>
/// The rules that say whether a requested update is still in flight, and what to do with a task whose
/// report never arrived (threat note 2026-11-07-panel-update-from-panel-threat-note.md section 5).
/// </summary>
/// <remarks>
/// The thresholds are the only place the panel decides how long it waits for root. A marker the root
/// unit has not picked up within <see cref="MarkerGrace"/> means the unit never started; a running record
/// older than <see cref="RunningFreshness"/> is a record a killed run left behind (the unit's own
/// timeout is shorter); and a ledger with no marker and no running record that is older than
/// <see cref="LedgerGrace"/> means the run ended and its report did not arrive.
/// </remarks>
public static class PanelUpdateLedgerPolicy
{
    /// <summary>How long a request marker may wait for the root unit before the request counts as never started.</summary>
    public static readonly TimeSpan MarkerGrace = TimeSpan.FromMinutes(5);

    /// <summary>How long root's running record is believed.</summary>
    public static readonly TimeSpan RunningFreshness = TimeSpan.FromHours(4);

    /// <summary>How long a ledger may exist with neither a marker nor a running record before its outcome counts as unknown.</summary>
    public static readonly TimeSpan LedgerGrace = TimeSpan.FromMinutes(2);

    /// <summary>Whether an update is queued or running.</summary>
    /// <param name="signals">What the file system shows now.</param>
    /// <returns>True when a young marker waits for root or root's running record is fresh.</returns>
    public static bool IsInProgress(PanelUpdateSignals signals)
    {
        ArgumentNullException.ThrowIfNull(signals);

        return signals.RunningFresh || (signals.MarkerAge is { } age && age < MarkerGrace);
    }

    /// <summary>Whether a release check the panel asked for is queued or running (issue #516).</summary>
    /// <param name="checkMarkerAge">How long ago the check marker was created, or null when there is none.</param>
    /// <returns>True when a check marker younger than <see cref="MarkerGrace"/> exists.</returns>
    /// <remarks>
    /// The check marker is removed by the root check unit itself when the check ends, successfully or not
    /// (<c>ExecStopPost=</c>), so it exists exactly while the check is queued or running and no separate
    /// running record is needed. The unit's own timeout is shorter than <see cref="MarkerGrace"/>: a marker
    /// older than that is one the unit never took, and is not a check in flight.
    /// </remarks>
    public static bool IsCheckInProgress(TimeSpan? checkMarkerAge)
    {
        return checkMarkerAge is { } age && age < MarkerGrace;
    }

    /// <summary>Decides what the reaper does with the open request, if there is one.</summary>
    /// <param name="signals">What the file system shows now.</param>
    /// <param name="ledgerAge">How long ago the ledger was written, or null when there is no ledger.</param>
    /// <returns>The verdict.</returns>
    public static PanelUpdateLedgerVerdict Decide(PanelUpdateSignals signals, TimeSpan? ledgerAge)
    {
        ArgumentNullException.ThrowIfNull(signals);

        if (ledgerAge is null || signals.RunningFresh)
        {
            return PanelUpdateLedgerVerdict.Leave;
        }

        if (signals.MarkerAge is { } markerAge)
        {
            return markerAge < MarkerGrace ? PanelUpdateLedgerVerdict.Leave : PanelUpdateLedgerVerdict.NeverStarted;
        }

        return ledgerAge.Value > LedgerGrace ? PanelUpdateLedgerVerdict.OutcomeUnknown : PanelUpdateLedgerVerdict.Leave;
    }
}
