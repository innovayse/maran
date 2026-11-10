namespace Maran.Modules.ServerMigration.Queries.ExportMigrationSummary;

/// <summary>Exports the summary of a job as a file (issue #197, phase 2).</summary>
/// <param name="JobId">The job.</param>
/// <param name="AsJson">Whether the file is JSON instead of CSV.</param>
public sealed record ExportMigrationSummaryQuery(Guid JobId, bool AsJson = false);
