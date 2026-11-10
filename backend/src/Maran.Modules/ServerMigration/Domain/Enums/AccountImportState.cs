namespace Maran.Modules.ServerMigration.Domain.Enums;

/// <summary>Where an import of one account stands, from "its package is accepted" to how it ended.</summary>
/// <remarks>
/// Stored by name. The state is the panel's record of what the import did, and the only states an account can be
/// left in by a failure are <see cref="RolledBack"/> (nothing remains) and <see cref="RolledBackIncomplete"/> (the
/// import's own account remains and the administrator is told).
/// </remarks>
public enum AccountImportState
{
    /// <summary>The package is verified and the agent holds the artifact; nothing about any account has changed.</summary>
    Staged,

    /// <summary>The job is creating the account and its resources.</summary>
    Running,

    /// <summary>The account exists and holds what the package carried.</summary>
    Completed,

    /// <summary>The job failed and the account it had created was deleted again; nothing remains.</summary>
    RolledBack,

    /// <summary>The job failed and deleting the account it had created also failed; the account remains.</summary>
    RolledBackIncomplete,

    /// <summary>An administrator discarded the staged package without starting it.</summary>
    Discarded,
}
