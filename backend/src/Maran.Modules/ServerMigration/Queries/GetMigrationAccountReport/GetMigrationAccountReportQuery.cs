namespace Maran.Modules.ServerMigration.Queries.GetMigrationAccountReport;

/// <summary>Reads the comparison report of one account's cut-over (issue #197, phase 2).</summary>
/// <param name="JobId">The job.</param>
/// <param name="AccountId">The account row.</param>
public sealed record GetMigrationAccountReportQuery(Guid JobId, Guid AccountId);
