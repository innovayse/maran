namespace Maran.Modules.ServerMigration.Common;

/// <summary>The dry run of a job: whether it can start, and every finding, conflicts first.</summary>
/// <param name="JobId">The job.</param>
/// <param name="CanStart">Whether no account has a conflict. Advice for the screen; starting judges again.</param>
/// <param name="Findings">Every finding.</param>
public sealed record MigrationJobReportDto(Guid JobId, bool CanStart, IReadOnlyList<MigrationAccountFindingDto> Findings);
