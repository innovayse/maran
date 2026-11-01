namespace Maran.Modules.Backups.Domain.Enums;

/// <summary>What an operator alert about the panel's unattended backups is reporting.</summary>
/// <remarks>
/// A closed set, one value per thing that used to happen silently (issue #134). Each is an EVENT and
/// not a condition: one failed run is one mail, so nothing here needs the raised/resolved episode
/// bookkeeping the Monitoring module keeps for a disk that stays full. The value's name is written
/// into the journal as the first half of the alert's subject, so a member is never renamed.
/// </remarks>
public enum BackupAlertKind
{
    /// <summary>A scheduled run reached the agent and ended without an archive.</summary>
    ScheduledRunFailed = 1,

    /// <summary>A scheduled run was not started because the panel could not use its destination.</summary>
    DestinationRefused = 2,

    /// <summary>A schedule started so long after its named hour that the run at that hour was missed.</summary>
    ScheduledRunMissed = 3,
}
