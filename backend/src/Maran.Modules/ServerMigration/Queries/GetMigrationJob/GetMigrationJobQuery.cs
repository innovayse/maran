namespace Maran.Modules.ServerMigration.Queries.GetMigrationJob;

/// <summary>Reads one migration job (issue #197, phase 2).</summary>
/// <param name="JobId">The job.</param>
public sealed record GetMigrationJobQuery(Guid JobId);
