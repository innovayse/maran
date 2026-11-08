namespace Maran.Modules.Monitoring.Domain.Enums;

/// <summary>
/// How a scheduled, unattended panel update ended, as the root command reports it to the panel
/// (issue #383). A closed set: a run that was skipped or refused changed nothing and is never reported.
/// </summary>
public enum PanelUpdateOutcome
{
    /// <summary>The release was installed and the health check passed.</summary>
    Applied,

    /// <summary>The update failed after the switch and the previous release is running again.</summary>
    RolledBack,

    /// <summary>The update failed and the previous release did not become healthy either.</summary>
    RollbackFailed,

    /// <summary>The run was interrupted after the release was switched; an operator must finish or revert it.</summary>
    Interrupted,
}
