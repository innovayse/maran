using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.CronService;
using Maran.Modules.Cron.Common;
using Maran.Modules.Cron.Domain.Entities;
using Maran.Modules.Cron.Domain.Enums;
using Maran.Modules.Cron.Domain.Policies;
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

    /// <summary>This module's audit journal.</summary>
    private readonly CronAuditJournal _journal;

    /// <summary>Where an agent refusal leaves its code and the entry id, and nothing else.</summary>
    private readonly ILogger<RunCronEntryCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="accounts">The owning account's system user name and state.</param>
    /// <param name="agent">The agent client that runs the entry.</param>
    /// <param name="dbContext">Where the run is recorded.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="logger">Where an agent refusal is reported, by code and entry id only.</param>
    public RunCronEntryCommandHandler(
        IAccountDirectory accounts,
        IAgentCronClient agent,
        CronDbContext dbContext,
        IClock clock,
        CronAuditJournal journal,
        ILogger<RunCronEntryCommandHandler> logger)
    {
        _accounts = accounts;
        _agent = agent;
        _dbContext = dbContext;
        _clock = clock;
        _journal = journal;
        _logger = logger;
    }

    /// <summary>Runs the entry now and records what it reported.</summary>
    /// <param name="command">The validated parameters; see <see cref="RunCronEntryCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The recorded run — or <c>AccountNotFound</c>, <c>CronEntrySuspended</c>, <c>CronEntryNotFound</c>,
    /// or <c>CronOperationFailed</c>.
    /// </returns>
    public async Task<Result<CronRunDto>> HandleAsync(RunCronEntryCommand command, CancellationToken cancellationToken)
    {
        var account = await _accounts.FindAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(
                command, Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound), cancellationToken);
        }

        if (account.State == AccountState.Suspended)
        {
            return await FailAsync(
                command, Error.Of(nameof(ErrorMessages.CronEntrySuspended), ErrorType.Conflict), cancellationToken);
        }

        var startedAt = _clock.UtcNow;
        var ran = await _agent.RunEntryAsync(account.Username, command.EntryId, cancellationToken);
        if (!ran.IsSuccess)
        {
            return await FailAsync(
                command,
                CronAgentErrorTranslator.Translate(_logger, ran.Error!, nameof(_agent.RunEntryAsync), command.EntryId),
                cancellationToken);
        }

        var run = new CronRun(
            Guid.NewGuid(),
            account.Id,
            command.EntryId,
            startedAt,
            ran.Value.DurationMilliseconds,
            ran.Value.ExitCode,
            LimitOf(ran.Value.Limit),
            ran.Value.Stdout,
            ran.Value.Stderr);

        // The runs beyond the retention are found BEFORE the new one is added, so the new one is never
        // among them; both changes are one SaveChanges, so the history never holds more than the bound.
        var stale = await _dbContext.CronRuns
            .Where(row => row.AccountId == account.Id && row.EntryId == command.EntryId)
            .OrderByDescending(row => row.StartedAt)
            .ThenByDescending(row => row.Id)
            .Skip(CronRunRetention.RunsPerEntry - 1)
            .ToListAsync(cancellationToken);
        _dbContext.CronRuns.RemoveRange(stale);
        _dbContext.CronRuns.Add(run);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            AuditActions.CronEntryRun, command.EntryId, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<CronRunDto>.Ok(CronRunMapper.From(run));
    }

    /// <summary>The module's own name for the agent's ceiling.</summary>
    /// <param name="limit">The ceiling the agent reported, or null.</param>
    /// <returns>The same ceiling in the module's vocabulary, or null.</returns>
    private static CronRunLimit? LimitOf(AgentCronRunLimit? limit)
    {
        return limit switch
        {
            AgentCronRunLimit.Runtime => CronRunLimit.Runtime,
            AgentCronRunLimit.Output => CronRunLimit.Output,
            _ => null,
        };
    }

    /// <summary>Journals a refused run and returns it as the typed failure.</summary>
    /// <param name="command">The run that was refused; its entry id is the journal's subject.</param>
    /// <param name="error">The typed failure to answer with.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<CronRunDto>> FailAsync(
        RunCronEntryCommand command,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AuditActions.CronEntryRun, command.EntryId, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<CronRunDto>.Fail(error);
    }
}
