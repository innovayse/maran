namespace Maran.Modules.ServerMigration.Domain.Enums;

/// <summary>Which pass of an account's transfer a run of the engine is.</summary>
public enum MigrationRunKind
{
    /// <summary>Create the account and copy everything once, with the source still serving.</summary>
    Initial,

    /// <summary>The delta at cut-over: copy again, delete what the source deleted, load the databases again, verify.</summary>
    CutOver,
}
