namespace Maran.Modules.ServerMigration.Domain.Enums;

/// <summary>Where a whole-server migration job is (issue #197, phase 2).</summary>
public enum MigrationJobState
{
    /// <summary>Accounts are chosen and mapped; nothing has been created. The only state in which the mapping is edited.</summary>
    Draft,

    /// <summary>The engine is creating and syncing accounts, up to the job's concurrency at a time.</summary>
    Running,

    /// <summary>The administrator paused it: every running transfer was stopped at a boundary and keeps its partial state.</summary>
    Paused,

    /// <summary>Every account ended, completed or not, and nothing is left to run.</summary>
    Completed,

    /// <summary>The administrator cancelled it; the accounts it had created were deleted again.</summary>
    Cancelled,
}
