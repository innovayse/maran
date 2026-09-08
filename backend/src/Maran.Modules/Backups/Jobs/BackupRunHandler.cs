using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Resources;
using Maran.Modules.Backups.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Maran.Modules.Backups.Jobs;

/// <summary>
/// Runs the backups the panel's schedules are due for, and asks for retention after each one that
/// succeeded (spec §11, R12).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the only writer of <see cref="BackupKind.Scheduled"/>, and there is no way to ask for
/// one over HTTP.</b> <c>CreateBackupCommand</c> carries no kind precisely so that a caller cannot
/// mark an ordinary backup as one whose retention rules differ; the kind is decided here, by the
/// panel, for a run nobody requested.
/// </para>
/// <para>
/// <b><see cref="BackupSchedule.MarkRunStarted"/> is written and COMMITTED before the first agent
/// call.</b> The scheduler ticks every five minutes and a backup takes minutes to hours, so a
/// schedule still marked unrun would be picked up again by the very next tick, on top of the run
/// already in flight. Committing first means the worst case is a night skipped — recoverable, and
/// visible as an unchanged <c>LastRunAt</c> on the screen — instead of two archives of one account
/// being written at once. The agent's per-account lock would refuse the second, but relying on that
/// would make a correctness property of the panel into a property of the agent's error message.
/// </para>
/// <para>
/// <b>Accounts are read through the unscoped listing, which is what an unattended pass has to
/// do.</b> <see cref="IAccountDirectory.FindAsync"/> applies the caller's tenant scope, and there is
/// no caller here — it would answer <c>null</c> for every account, every night, for ever, which is
/// the silent-nothing failure that scoping an unattended query always produces.
/// <see cref="IAccountDirectory.ListAsync"/> is unscoped by design and its own remarks require every
/// consumer to be gated to administrators; this consumer is not gated to anybody because it is not
/// reachable by anybody — it runs off the panel's own timer, has no HTTP surface, and nothing it
/// returns leaves this process.
/// </para>
/// <para>
/// <b>One failing account does not stop the sweep.</b> Each account's run is enclosed, so an agent
/// refusal for one customer still leaves the rest of the host backed up — the opposite arrangement
/// makes the account with the biggest database the reason nobody else has a backup.
/// </para>
/// </remarks>
public sealed class BackupRunHandler
{
    /// <summary>Pre-compiled log delegate for a finished sweep.</summary>
    private static readonly Action<ILogger, int, int, Exception?> LogSweep =
        LoggerMessage.Define<int, int>(
            LogLevel.Information,
            new EventId(1, nameof(BackupRunHandler)),
            "Scheduled backup sweep ran {Due} due schedules and started {Started} account backups");

    /// <summary>Pre-compiled log delegate for one account's run that could not be completed.</summary>
    private static readonly Action<ILogger, Guid, Exception?> LogAccountFailed =
        LoggerMessage.Define<Guid>(
            LogLevel.Error,
            new EventId(2, nameof(BackupRunHandler)),
            "A scheduled backup of account {AccountId} could not be completed; the sweep continues");

    /// <summary>Pre-compiled log delegate for a schedule whose destination the panel cannot use.</summary>
    private static readonly Action<ILogger, Guid, string, Exception?> LogRefused =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Error,
            new EventId(3, nameof(BackupRunHandler)),
            "The scheduled backup of account {AccountId} was not started: its destination was refused "
            + "with {Code}, so no run was attempted and the schedule is due again at its next "
            + "occurrence");

    /// <summary>The Backups module's database context.</summary>
    private readonly BackupsDbContext _dbContext;

    /// <summary>The unscoped window onto every account on the host.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The stream consumer that reduces one run to a single outcome.</summary>
    private readonly BackupRunner _runner;

    /// <summary>Resolves which destination each schedule writes to.</summary>
    private readonly BackupDestinationResolver _destinations;

    /// <summary>This module's audit journal.</summary>
    private readonly BackupAuditJournal _journal;

    /// <summary>The injected time source; never the ambient clock (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>The panel-wide task journal, so an operator can see a nightly run in the feed.</summary>
    private readonly ITaskRecorder _tasks;

    /// <summary>The correlation id this sweep's tasks are recorded under.</summary>
    private readonly ICorrelationIdAccessor _correlationIds;

    /// <summary>Where the retention request for a completed run is published.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Where the outcome of each sweep is reported.</summary>
    private readonly ILogger<BackupRunHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Backups module's database context.</param>
    /// <param name="accounts">The unscoped window onto every account on the host.</param>
    /// <param name="runner">The stream consumer that drives one run.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The injected time source due-ness is measured against.</param>
    /// <param name="tasks">The panel-wide task journal.</param>
    /// <param name="correlationIds">The correlation id this sweep's tasks are recorded under.</param>
    /// <param name="destinations">Resolves which destination each schedule writes to.</param>
    /// <param name="bus">Where the retention request for a completed run is published.</param>
    /// <param name="logger">Where the outcome of each sweep is reported.</param>
    public BackupRunHandler(
        BackupsDbContext dbContext,
        IAccountDirectory accounts,
        BackupRunner runner,
        BackupDestinationResolver destinations,
        BackupAuditJournal journal,
        IClock clock,
        ITaskRecorder tasks,
        ICorrelationIdAccessor correlationIds,
        IMessageBus bus,
        ILogger<BackupRunHandler> logger)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _runner = runner;
        _destinations = destinations;
        _journal = journal;
        _clock = clock;
        _tasks = tasks;
        _correlationIds = correlationIds;
        _bus = bus;
        _logger = logger;
    }

    /// <summary>Runs one sweep over the panel's schedules.</summary>
    /// <param name="message">The scheduled trigger; it carries no parameters.</param>
    /// <param name="cancellationToken">Cancels the sweep between accounts.</param>
    /// <returns>How many account backups were started; zero is the ordinary outcome on most ticks.</returns>
    public async Task<int> HandleAsync(BackupRunRequested message, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var due = (await EnabledSchedulesAsync(cancellationToken))
            .Where(schedule => { return schedule.IsDue(now); })
            .ToList();

        if (due.Count == 0)
        {
            return 0;
        }

        var accounts = await _accounts.ListAsync(cancellationToken);
        var started = 0;

        foreach (var schedule in due)
        {
            // Written and committed before anything runs, so the next tick — five minutes from now,
            // while this sweep may still be inside its first archive — does not select it again.
            schedule.MarkRunStarted(now);
            await _dbContext.SaveChangesAsync(cancellationToken);

            foreach (var account in Targets(schedule, accounts))
            {
                started += await RunOneAsync(
                    account, schedule.DestinationId, schedule.RetainCount, cancellationToken);
            }
        }

        LogSweep(_logger, due.Count, started, null);

        return started;
    }

    /// <summary>The accounts one schedule covers.</summary>
    /// <param name="schedule">The due schedule.</param>
    /// <param name="accounts">Every account on the host.</param>
    /// <returns>Every account for the host-wide policy, or the one account an override names.</returns>
    /// <remarks>
    /// An override naming an account that no longer exists yields nothing, and the schedule is still
    /// stamped as run. Such a row should not exist — the account cascade removes it — so producing
    /// no target rather than an error is the honest reading: there is nothing to back up.
    /// </remarks>
    private static IEnumerable<AccountSnapshot> Targets(
        BackupSchedule schedule,
        IReadOnlyList<AccountSnapshot> accounts)
    {
        if (schedule.AccountId is null)
        {
            return accounts;
        }

        return accounts.Where(account => { return account.Id == schedule.AccountId.Value; });
    }

    /// <summary>Reads every enabled schedule, ignoring the tenant filter this pass cannot satisfy.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The enabled schedules, tracked so the run stamp can be saved.</returns>
    private Task<List<BackupSchedule>> EnabledSchedulesAsync(CancellationToken cancellationToken)
    {
#pragma warning disable RS0030 // unattended sweep with no principal; scoped, it would select nothing, every tick
        return _dbContext.BackupSchedules
            .IgnoreQueryFilters()
            .Where(schedule => schedule.Enabled)
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030
    }

    /// <summary>Takes one account's scheduled backup and asks for retention if it succeeded.</summary>
    /// <param name="account">The account to back up.</param>
    /// <param name="destinationId">The destination the schedule names, or <c>null</c> for the default.</param>
    /// <param name="retainCount">How many successful backups the schedule keeps.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>One when a run was started, zero when it could not be.</returns>
    /// <remarks>
    /// The row is written Running before the agent is called and moved from the one terminal outcome
    /// the run produced, exactly as <c>CreateBackupCommandHandler</c> does — never from "nothing
    /// threw". A failed run publishes NO retention request, which is R12: pruning after a failure is
    /// how one bad night costs the oldest copy that still worked.
    /// </remarks>
    private async Task<int> RunOneAsync(
        AccountSnapshot account,
        Guid? destinationId,
        int retainCount,
        CancellationToken cancellationToken)
    {
        // A destination the panel cannot use stops THIS account's run and no other. The schedule has
        // already been stamped as run, which is the same trade the rest of this sweep makes: a
        // schedule pointing at a destination that has gone is a configuration fault an operator has
        // to fix, and retrying it every five minutes against a root process is not how they find out.
        var destination = await _destinations.ResolveAsync(destinationId, cancellationToken);
        if (!destination.IsSuccess)
        {
            LogRefused(_logger, account.Id, destination.Error!.Code, null);
            return 0;
        }

        var backup = new Backup(
            Guid.NewGuid(), account.Id, destination.Value!.Id, BackupKind.Scheduled, _clock.UtcNow);

        _dbContext.Backups.Add(backup);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // The account, not the backup id — the same subject the manual create and every other kind
        // of task records, and the one ITaskRecorder.BeginAsync asks for.
        var taskId = await _tasks.BeginAsync(
            TaskKinds.BackupCreate, account.Username, _correlationIds.CorrelationId, cancellationToken);

        try
        {
            var outcome = await _runner.RunAsync(
                account.Username, backup.Id, destination.Value.Agent, taskId, cancellationToken);

            if (outcome.Succeeded)
            {
                backup.Completed(outcome.SizeBytes, outcome.Sha256, outcome.DatabaseCount, _clock.UtcNow);
            }
            else
            {
                backup.Failed(outcome.FailureCode, _clock.UtcNow);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            await _journal.RecordScheduledAsync(
                AuditActions.BackupCreated, backup.Id, outcome.Succeeded, cancellationToken);

            if (!outcome.Succeeded)
            {
                await _tasks.FailAsync(taskId, outcome.FailureCode, cancellationToken);
                return 1;
            }

            await _tasks.CompleteAsync(taskId, cancellationToken);
            await _bus.PublishAsync(new RetentionRequested(account.Id, retainCount));

            return 1;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // One account's failure is not the sweep's. Anything the runner did not turn into a
            // typed outcome — a dropped socket, a container refusing a connection — would otherwise
            // abandon every account after this one in the list, and the account it stopped at would
            // be whichever one the agent happened to be unhappy about that night.
            LogAccountFailed(_logger, account.Id, exception);

            await AbandonAsync(backup, taskId, cancellationToken);

            return 0;
        }
    }

    /// <summary>Closes the row and the task of a run that threw before it could report an outcome.</summary>
    /// <param name="backup">The row opened for the run.</param>
    /// <param name="taskId">The panel task opened for the run.</param>
    /// <param name="cancellationToken">Cancels the writes.</param>
    /// <remarks>
    /// <para>
    /// Without this the row stays <c>Running</c> for ever — and a running backup may not be deleted,
    /// by the entity's own rule, so an operator would be left with a row nothing could remove and a
    /// screen showing a backup permanently in progress. The task would be closed at the next restart
    /// by the startup reconciler; nothing closes the row but this.
    /// </para>
    /// <para>
    /// Its own failure is swallowed to a log line, and that is the one place in this handler where
    /// swallowing is right: this is already the failure path, and letting it throw would replace a
    /// stuck row with an abandoned sweep.
    /// </para>
    /// </remarks>
    private async Task AbandonAsync(Backup backup, Guid taskId, CancellationToken cancellationToken)
    {
        try
        {
            backup.Failed(nameof(ErrorMessages.BackupScheduledRunAborted), _clock.UtcNow);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _tasks.FailAsync(taskId, nameof(ErrorMessages.BackupScheduledRunAborted), cancellationToken);

            await _journal.RecordScheduledAsync(
                AuditActions.BackupCreated, backup.Id, succeeded: false, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogAccountFailed(_logger, backup.AccountId, exception);
        }
    }
}
