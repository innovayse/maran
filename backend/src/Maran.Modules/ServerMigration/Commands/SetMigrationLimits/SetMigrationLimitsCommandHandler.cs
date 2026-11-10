using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Extensions;
using Maran.Modules.ServerMigration.Jobs;
using Maran.Modules.ServerMigration.Mappers;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Maran.Modules.ServerMigration.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Localization;
using Wolverine;

namespace Maran.Modules.ServerMigration.Commands.SetMigrationLimits;

/// <summary>
/// Handles <see cref="SetMigrationLimitsCommand"/>: stores the limits the scheduler re-reads every tick and a
/// transfer picks up at its next slice (issue #197, phase 2).
/// </summary>
public sealed class SetMigrationLimitsCommandHandler
{
    /// <summary>This module's database.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>The module's display names, in the caller's language.</summary>
    private readonly IStringLocalizer<DisplayNames> _names;

    /// <summary>Sends the tick.</summary>
    private readonly IMessageBus _bus;

    /// <summary>This module's audit journal.</summary>
    private readonly ServerMigrationAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">This module's database.</param>
    /// <param name="bus">Sends the tick.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="names">The module's display names, in the caller's language.</param>
    public SetMigrationLimitsCommandHandler(
        ServerMigrationDbContext dbContext,
        IMessageBus bus,
        ServerMigrationAuditJournal journal,
        IStringLocalizer<DisplayNames> names)
    {
        _dbContext = dbContext;
        _bus = bus;
        _journal = journal;
        _names = names;
    }

    /// <summary>Stores the limits.</summary>
    /// <param name="command">The request.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The job; <c>MigrationJobNotFound</c> or <c>MigrationJobStateInvalid</c> otherwise.</returns>
    public async Task<Result<MigrationJobDto>> HandleAsync(SetMigrationLimitsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var job = await _dbContext.MigrationJobs.FirstOrDefaultAsync(row => row.Id == command.JobId, cancellationToken);
        if (job is null)
        {
            return Result<MigrationJobDto>.Fail(Error.Of(nameof(ErrorMessages.MigrationJobNotFound), ErrorType.NotFound));
        }

        var changed = job.ChangeLimits(command.Concurrency, command.BandwidthLimitKib);
        if (!changed.IsSuccess)
        {
            return Result<MigrationJobDto>.Fail(changed.Error!);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _journal.RecordSuccessAsync(
            MigrationAuditActions.JobLimitsChanged, job.Name, command.IpAddress, command.UserAgent, cancellationToken);
        await _bus.PublishAsync(new MigrationJobTickRequested(job.Id));

        return Result<MigrationJobDto>.Ok(
            MigrationJobMapper.ToDto(job, await _dbContext.CountStatesAsync(job.Id, cancellationToken), _names));
    }
}
