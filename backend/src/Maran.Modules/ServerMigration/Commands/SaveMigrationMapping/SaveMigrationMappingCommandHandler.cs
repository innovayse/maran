using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Maran.Modules.ServerMigration.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.ServerMigration.Commands.SaveMigrationMapping;

/// <summary>Handles <see cref="SaveMigrationMappingCommand"/>: maps accounts of a draft job (issue #197, phase 2).</summary>
/// <remarks>
/// Only a draft job is edited: once the engine runs, an account's plan and owner are what its account was created
/// with, and changing the row would say something the account does not. The plan and the reseller are not checked for
/// existence here; the dry run and the start judge them with the importers, which know the plans.
/// </remarks>
public sealed class SaveMigrationMappingCommandHandler
{
    /// <summary>This module's database.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>This module's audit journal.</summary>
    private readonly ServerMigrationAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">This module's database.</param>
    /// <param name="journal">This module's audit journal.</param>
    public SaveMigrationMappingCommandHandler(ServerMigrationDbContext dbContext, ServerMigrationAuditJournal journal)
    {
        _dbContext = dbContext;
        _journal = journal;
    }

    /// <summary>Applies the mapping.</summary>
    /// <param name="command">The request.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>How many accounts were changed; <c>MigrationJobNotFound</c>, <c>MigrationJobNotEditable</c> or <c>MigrationJobAccountNotFound</c> otherwise.</returns>
    public async Task<Result<int>> HandleAsync(SaveMigrationMappingCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var job = await _dbContext.MigrationJobs.AsNoTracking().FirstOrDefaultAsync(row => row.Id == command.JobId, cancellationToken);
        if (job is null)
        {
            return Result<int>.Fail(Error.Of(nameof(ErrorMessages.MigrationJobNotFound), ErrorType.NotFound));
        }

        if (!job.IsEditable)
        {
            return Result<int>.Fail(Error.Of(nameof(ErrorMessages.MigrationJobNotEditable), ErrorType.Conflict));
        }

        var rows = await _dbContext.MigrationJobAccounts
            .Where(row => row.JobId == job.Id && (command.AccountIds.Count == 0 || command.AccountIds.Contains(row.Id)))
            .ToListAsync(cancellationToken);
        if (command.AccountIds.Count > 0 && rows.Count != command.AccountIds.Count)
        {
            return Result<int>.Fail(Error.Of(nameof(ErrorMessages.MigrationJobAccountNotFound), ErrorType.NotFound));
        }

        foreach (var row in rows)
        {
            row.Remap(
                command.PlanId,
                command.ResellerId,
                command.ClearReseller,
                command.OwnerEmail,
                command.OwnerLanguage,
                command.PrimaryDomain,
                command.PullGrant?.Reveal());
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _journal.RecordSuccessAsync(
            MigrationAuditActions.JobMapped, job.Name, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<int>.Ok(rows.Count);
    }
}
