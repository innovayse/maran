namespace Maran.Modules.ServerMigration.Domain.Enums;

/// <summary>Where one source account is in a migration job (issue #197, phase 2).</summary>
public enum MigrationAccountState
{
    /// <summary>Waiting for the scheduler to start it.</summary>
    Pending,

    /// <summary>The account is being created and its first sync (files, then databases) is running.</summary>
    Running,

    /// <summary>The initial sync is done. The source keeps serving; the administrator cuts over when ready.</summary>
    Synced,

    /// <summary>The final delta (files deleted at the source are deleted here, databases are loaded again) and the verification are running.</summary>
    CuttingOver,

    /// <summary>Cut over and verified: this server holds the account's last state.</summary>
    Completed,

    /// <summary>Stopped at a boundary by a pause, with its partial state kept.</summary>
    Paused,

    /// <summary>The final delta or the verification failed. The account is kept (it was already synced); a retry runs the cut-over again.</summary>
    Failed,

    /// <summary>Cancelled by the administrator; the account it created was deleted again.</summary>
    Cancelled,

    /// <summary>Failed before its first sync finished and the account it created was deleted again.</summary>
    RolledBack,

    /// <summary>Failed and the rollback could not finish: the account the row names remains.</summary>
    RolledBackIncomplete,
}
