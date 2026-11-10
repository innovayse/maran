namespace Maran.Modules.ServerMigration.Domain.Enums;

/// <summary>What an administrator asks of one account of a job.</summary>
public enum MigrationAccountAction
{
    /// <summary>Run a failed account again: a rolled-back one from the start, a failed cut-over from its cut-over.</summary>
    Retry,

    /// <summary>End this account's transfer and delete the account it created.</summary>
    Cancel,

    /// <summary>Run the final delta and the verification of a synced account.</summary>
    CutOver,
}
