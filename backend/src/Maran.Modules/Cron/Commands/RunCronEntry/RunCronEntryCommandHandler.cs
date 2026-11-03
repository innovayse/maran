using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.CronService;
using Maran.Modules.Cron.Common;
using Maran.Modules.Cron.Domain.Entities;
using Maran.Modules.Cron.Domain.Enums;
using Maran.Modules.Cron.Domain.Policies;
using Maran.Modules.Cron.Domain.ValueObjects;
using Maran.Modules.Cron.Mappers;
using Maran.Modules.Cron.Persistence;
using Maran.Modules.Cron.Resources;
using Maran.Modules.Cron.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Cron.Commands.RunCronEntry;

/// <summary>
/// Handles <see cref="RunCronEntryCommand"/>: resolves the account, asks the agent to run the entry
/// now, records the run and keeps only the newest <see cref="CronRunRetention.RunsPerEntry"/>.
/// </summary>
/// <remarks>
/// <para>
/// A suspended account is refused here, before the agent is asked, and the agent refuses it again from
/// the crontab's own suspension marker: the panel's state and the host's are two answers, and either
/// one is enough to say no. A DISABLED entry runs — that is the customer's switch for the schedule,
/// and this run is one they asked for.
/// </para>
/// <para>
/// The job's own failure is a recorded run, not a failed request: a script that exits 1 is exactly
/// what a customer is running it by hand to find out. Only the agent refusing or failing to start the
/// run is a failure, and then nothing is recorded, because nothing ran.
/// </para>
/// <para>
/// The run is recorded AFTER the agent answered, so a run whose answer never arrived — the panel's
/// wait timed out, the process died — is not in the history. That is the honest gap: the panel did
/// not learn how it ended, and a row saying otherwise would be invented.
/// </para>
/// <para>
/// <b>Concurrency (issue #333).</b> A slot is taken from <see cref="CronRunSlots"/> after the
/// account is known to be able to run and BEFORE the agent is called, and given back when the call
/// is over. A run past the account's bound or the panel's is refused here, so a customer clicking
/// "run" in a loop never reaches the root process.
/// </para>
/// <para>
/// <b>Audit before history (issue #335).</b> Once the agent has answered, the run HAS happened, and
/// the journal says so before the history row is written: the entry is written first, and both
/// writes use <see cref="CancellationToken.None"/> rather than the request's token, because a caller
/// who closes the connection while the job runs does not un-run it. The row is the second write
/// because it is the one that can still fail (the output is the customer's, and
/// <see cref="CronRunOutputPolicy"/> removes the one character a text column refuses, but a database
/// fault is still possible); a run missing from the history is a gap, a run missing from the journal
/// is a hole in the record of what root did on a customer's behalf. A caller who goes away while the
/// agent still holds the run leaves a failure entry (the run was asked for; its end is unknown).
/// </para>
/// </remarks>
public sealed class RunCronEntryCommandHandler
{
    /// <summary>The one window onto the owning account's system user name and state.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which runs the entry as the account.</summary>
    private readonly IAgentCronClient _agent;

    /// <summary>Where the run is recorded.</summary>
    private readonly CronDbContext _dbContext;

    /// <summary>The panel's clock, for when the run started.</summary>
    private readonly IClock _clock;

    /// <summary>The bound on runs in flight, per account and in total.</summary>
    private readonly CronRunSlots _slots;

    /// <summary>This module's audit journal.</summary>
    private readonly CronAuditJournal _journal;

    /// <summary>Where an agent refusal leaves its code and the entry id, and nothing else.</summary>
    private readonly ILogger<RunCronEntryCommandHandler> _logger;

    /// <summary>Names the recorded run's outcome in the request's culture.</summary>
    private readonly CronRunDisplayNames _names;

    /// <summary>Creates the handler.</summary>
    /// <param name="accounts">The owning account's system user name and state.</param>
    /// <param name="agent">The agent client that runs the entry.</param>
    /// <param name="dbContext">Where the run is recorded.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="slots">The bound on runs in flight, per account and in total.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="logger">Where an agent refusal is reported, by code and entry id only.</param>
    /// <param name="names">Names the recorded run's outcome in the request's culture.</param>
    public RunCronEntryCommandHandler(
        IAccountDirectory accounts,
        IAgentCronClient agent,
        CronDbContext dbContext,
        IClock clock,
        CronRunSlots slots,
        CronAuditJournal journal,
        ILogger<RunCronEntryCommandHandler> logger,
        CronRunDisplayNames names)
    {
        _accounts = accounts;
        _agent = agent;
        _dbContext = dbContext;
        _clock = clock;
        _slots = slots;
        _journal = journal;
        _logger = logger;
        _names = names;
    }

    /// <summary>Runs the entry now and records what it reported.</summary>
    /// <param name="command">The validated parameters; see <see cref="RunCronEntryCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The recorded run — or <c>AccountNotFound</c>, <c>CronEntrySuspended</c>, <c>CronEntryNotFound</c>,
    /// <c>CronRunAccountLimitReached</c>, <c>CronRunServerBusy</c>, or <c>CronOperationFailed</c>.
    /// </returns>
    public async Task<Result<CronRunDto>> HandleAsync(RunCronEntryCommand command, CancellationToken cancellationToken)
    {
        var account = await _accounts.FindAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(
                command, Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound));
        }

        if (account.State == AccountState.Suspended)
        {
            return await FailAsync(command, Error.Of(nameof(ErrorMessages.CronEntrySuspended), ErrorType.Conflict));
        }

        var slot = _slots.TryAcquire(account.Id);
        if (!slot.IsSuccess)
        {
            return await FailAsync(command, slot.Error!);
        }

        using var lease = slot.Value;
        var startedAt = _clock.UtcNow;
        Result<AgentCronRun> ran;
        try
        {
            ran = await _agent.RunEntryAsync(account.Username, command.EntryId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller went away while the run was in the agent's hands. The run was asked for, so it is
            // journalled — as not having taken effect, because the panel never learned how it ended — and
            // the cancellation then travels on unchanged.
            await FailAsync(command, Error.Of(nameof(ErrorMessages.CronOperationFailed), ErrorType.Failure));
            throw;
        }

        if (!ran.IsSuccess)
        {
            return await FailAsync(
                command,
                CronAgentErrorTranslator.Translate(_logger, ran.Error!, nameof(_agent.RunEntryAsync), command.EntryId));
        }

        // The journal first, and nothing after the agent's answer takes the request's token: the run
        // has happened, and a caller who has gone away does not un-run it.
        await _journal.RecordSuccessAsync(
            CronAuditActions.CronEntryRun, command.EntryId, command.IpAddress, command.UserAgent, CancellationToken.None);

        var run = new CronRun(
            Guid.NewGuid(),
            account.Id,
            command.EntryId,
            startedAt,
            ran.Value.DurationMilliseconds,
            EndOf(ran.Value.End),
            ran.Value.Stdout,
            ran.Value.Stderr);

        // The runs beyond the retention are found BEFORE the new one is added, so the new one is never
        // among them; both changes are one SaveChanges, so the history never holds more than the bound.
        var stale = await _dbContext.CronRuns
            .Where(row => row.AccountId == account.Id && row.EntryId == command.EntryId)
            .OrderByDescending(row => row.StartedAt)
            .ThenByDescending(row => row.Id)
            .Skip(CronRunRetention.RunsPerEntry - 1)
            .ToListAsync(CancellationToken.None);
        _dbContext.CronRuns.RemoveRange(stale);
        _dbContext.CronRuns.Add(run);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        return Result<CronRunDto>.Ok(CronRunMapper.From(run, _names.Of(run.Outcome)));
    }

    /// <summary>The module's own name for how the agent says the run ended.</summary>
    /// <param name="end">How the agent reported the run ended.</param>
    /// <returns>The same ending in the module's vocabulary.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An ending this module has no name for.</exception>
    private static CronRunEnd EndOf(AgentCronRunEnd end)
    {
        return end switch
        {
            AgentCronRunExited exited => new CronRunExited(exited.ExitCode),
            AgentCronRunWithoutStatus => new CronRunEndedWithoutStatus(),
            AgentCronRunStopped { Limit: AgentCronRunLimit.Runtime } => new CronRunStopped(CronRunLimit.Runtime),
            AgentCronRunStopped { Limit: AgentCronRunLimit.Output } => new CronRunStopped(CronRunLimit.Output),
            _ => throw new ArgumentOutOfRangeException(nameof(end), end, "Unknown cron run ending."),
        };
    }

    /// <summary>
    /// Journals a refused run and returns it as the typed failure. The write does not take the request's
    /// token: a refusal is a record, and a caller who has gone away does not un-refuse it.
    /// </summary>
    /// <param name="command">The run that was refused; its entry id is the journal's subject.</param>
    /// <param name="error">The typed failure to answer with.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<CronRunDto>> FailAsync(RunCronEntryCommand command, Error error)
    {
        await _journal.RecordFailureAsync(
            CronAuditActions.CronEntryRun, command.EntryId, command.IpAddress, command.UserAgent, CancellationToken.None);

        return Result<CronRunDto>.Fail(error);
    }
}
