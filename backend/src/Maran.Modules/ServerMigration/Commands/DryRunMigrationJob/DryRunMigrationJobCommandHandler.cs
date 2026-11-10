using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Maran.Modules.ServerMigration.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.ServerMigration.Commands.DryRunMigrationJob;

/// <summary>
/// Handles <see cref="DryRunMigrationJobCommand"/>: the dry run of a whole-server migration, with conflicts and no
/// rename (decision D1) (issue #197, phase 2).
/// </summary>
/// <remarks>
/// Nothing about any account, site, database or login changes: every importer's <c>PlanAsync</c> reads and no more.
/// The sentences are produced here, in the caller's language; the screen only renders them. It is a command and not a
/// query because it is audited, and it is the same judgement the start repeats.
/// </remarks>
public sealed class DryRunMigrationJobCommandHandler
{
    /// <summary>This module's database.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>Judges the accounts.</summary>
    private readonly MigrationJobPlanner _planner;

    /// <summary>Resolves a finding code to its sentence.</summary>
    private readonly IErrorTextProvider _texts;

    /// <summary>This module's audit journal.</summary>
    private readonly ServerMigrationAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">This module's database.</param>
    /// <param name="planner">Judges the accounts.</param>
    /// <param name="texts">Resolves a finding code to its sentence.</param>
    /// <param name="journal">This module's audit journal.</param>
    public DryRunMigrationJobCommandHandler(
        ServerMigrationDbContext dbContext,
        MigrationJobPlanner planner,
        IErrorTextProvider texts,
        ServerMigrationAuditJournal journal)
    {
        _dbContext = dbContext;
        _planner = planner;
        _texts = texts;
        _journal = journal;
    }

    /// <summary>Judges the job.</summary>
    /// <param name="command">The request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The report, or <c>MigrationJobNotFound</c>.</returns>
    public async Task<Result<MigrationJobReportDto>> HandleAsync(
        DryRunMigrationJobCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var job = await _dbContext.MigrationJobs.AsNoTracking().FirstOrDefaultAsync(row => row.Id == command.JobId, cancellationToken);
        if (job is null)
        {
            return Result<MigrationJobReportDto>.Fail(Error.Of(nameof(ErrorMessages.MigrationJobNotFound), ErrorType.NotFound));
        }

        var accounts = await _dbContext.MigrationJobAccounts.AsNoTracking()
            .Where(row => row.JobId == job.Id && row.State == MigrationAccountState.Pending)
            .ToListAsync(cancellationToken);
        var findings = await _planner.PlanAsync(accounts, cancellationToken);
        var rows = findings
            .Select(row =>
            {
                return new MigrationAccountFindingDto(
                row.SourceAccountName,
                row.Finding.ModuleId,
                row.Finding.Severity.ToString(),
                row.Finding.Code,
                _texts.Resolve(row.Finding.Code),
                row.Finding.Subject);
            })
            .ToList();

        await _journal.RecordSuccessAsync(
            MigrationAuditActions.JobPlanned, job.Name, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<MigrationJobReportDto>.Ok(new MigrationJobReportDto(
            job.Id,
            accounts.Count > 0 && rows.All(row =>
            {
                return row.Severity != nameof(MigrationFindingSeverity.Conflict);
            }),
            rows));
    }
}
