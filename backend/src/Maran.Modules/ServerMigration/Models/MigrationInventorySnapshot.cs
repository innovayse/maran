namespace Maran.Modules.ServerMigration.Models;

/// <summary>
/// What the discovery found for one source account, copied into the job so that running the discovery again
/// cannot change a job in progress.
/// </summary>
/// <param name="Sites">The account's sites.</param>
/// <param name="Databases">The account's databases.</param>
public sealed record MigrationInventorySnapshot(
    IReadOnlyList<MigrationSnapshotSite> Sites,
    IReadOnlyList<MigrationSnapshotDatabase> Databases);
