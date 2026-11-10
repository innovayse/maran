namespace Maran.Modules.ServerMigration.Queries.GetMigrationDnsAdvice;

/// <summary>Asks where the domains of a job resolve now (issue #197, phase 2, decision D19).</summary>
/// <param name="JobId">The job.</param>
public sealed record GetMigrationDnsAdviceQuery(Guid JobId);
