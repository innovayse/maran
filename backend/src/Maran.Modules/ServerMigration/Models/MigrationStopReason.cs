namespace Maran.Modules.ServerMigration.Models;

/// <summary>Why a running transfer was asked to stop; the engine ends the account differently for each.</summary>
public enum MigrationStopReason
{
    /// <summary>The job was paused: the partial state is kept so that a resume continues.</summary>
    Pause,

    /// <summary>The account or job was cancelled: the account that was created is deleted again.</summary>
    Cancel,
}
