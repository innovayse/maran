namespace Maran.Modules.ServerMigration.Jobs;

/// <summary>
/// The message that asks the scheduler to look at one job again: start accounts if slots are free, or complete the
/// job if every account ended (issue #197, phase 2). It carries an id and nothing else.
/// </summary>
/// <param name="JobId">The job.</param>
public sealed record MigrationJobTickRequested(Guid JobId);
