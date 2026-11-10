using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Jobs;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Sdk.Contracts;
using Wolverine;

namespace Maran.Modules.ServerMigration.Services;

/// <summary>
/// Decides, for one running job, which pending accounts start now (issue #197, phase 2; decision D20).
/// </summary>
/// <remarks>
/// <para>
/// <b>The limit is read every tick</b>, from the job row, so raising or lowering it while the job runs takes effect
/// at the next account that ends or the next time the administrator changes it; a lower limit never stops an account
/// that is already running. The registry's gate serialises the decision: two ticks that both read "one slot free"
/// would otherwise both start an account.
/// </para>
/// <para>
/// A tick marks an account running BEFORE it sends the message that runs it, in the same save, so a second tick cannot
/// pick it again; the account's row is what the slots are counted from. A job with nothing pending, nothing running
/// and nothing synced and waiting is complete.
/// </para>
/// </remarks>
public sealed class MigrationJobScheduler
{
    /// <summary>The module's database context.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>Serialises decisions about one job.</summary>
    private readonly MigrationRunRegistry _registry;

    /// <summary>Sends the messages that run accounts.</summary>
    private readonly IMessageBus _bus;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>Records the completion.</summary>
    private readonly ServerMigrationAuditJournal _journal;

    /// <summary>Creates the scheduler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="registry">Serialises decisions about one job.</param>
    /// <param name="bus">Sends the messages that run accounts.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="journal">Records the completion.</param>
    public MigrationJobScheduler(
        ServerMigrationDbContext dbContext,
        MigrationRunRegistry registry,
        IMessageBus bus,
        IClock clock,
        ServerMigrationAuditJournal journal)
    {
        _dbContext = dbContext;
        _registry = registry;
        _bus = bus;
        _clock = clock;
        _journal = journal;
    }

    /// <summary>Starts what the limit allows, or completes the job.</summary>
    /// <param name="jobId">The job.</param>
    /// <param name="cancellationToken">Cancels the decision.</param>
    /// <returns>Resolves when the messages are sent.</returns>
    public async Task TickAsync(Guid jobId, CancellationToken cancellationToken)
    {
        using var gate = await _registry.EnterAsync(jobId, cancellationToken);

        var job = await _dbContext.MigrationJobs.FirstOrDefaultAsync(row => row.Id == jobId, cancellationToken);
        if (job is null || !job.IsRunning)
        {
            return;
        }

        var accounts = await _dbContext.MigrationJobAccounts
            .Where(row => row.JobId == jobId)
            .OrderBy(row => row.SourceAccountName)
            .ToListAsync(cancellationToken);
        var active = accounts.Count(row =>
        {
            return row.State is MigrationAccountState.Running or MigrationAccountState.CuttingOver;
        });
        var free = Math.Max(0, job.Concurrency - active);
        var started = accounts.Where(row =>
        {
            return row.State == MigrationAccountState.Pending;
        }).Take(free).ToList();

        foreach (var account in started)
        {
            account.BeginRun(_clock.UtcNow);
        }

        if (started.Count == 0 && active == 0 && accounts.All(row =>
        {
            return row.HasEnded;
        }))
        {
            job.Complete(_clock.UtcNow);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _journal.RecordSuccessAsync(
                MigrationAuditActions.JobCompleted, job.Name, string.Empty, string.Empty, cancellationToken);

            return;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        foreach (var account in started)
        {
            await _bus.PublishAsync(new MigrationAccountTransferRequested(account.Id, MigrationRunKind.Initial));
        }
    }
}
