using Maran.Agent.Client.Interfaces;
using Maran.Modules.Cron.Common;
using Maran.Modules.Cron.Mappers;
using Maran.Modules.Cron.Resources;
using Maran.Modules.Cron.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Cron.Commands.CreateCronEntry;

/// <summary>
/// Handles <see cref="CreateCronEntryCommand"/>: resolves the account, refuses an entry the plan
/// does not allow, and installs the rest through the agent.
/// </summary>
/// <remarks>
/// <para>
/// <b>The plan limit is counted against the AGENT'S listing, not against a panel table, because
/// there is no panel table and there must not be one.</b> The crontab is the record: the account
/// owns it and can add entries directly over SFTP, so a count of rows the panel had installed would
/// be a count of part of the crontab, and a customer at their limit could pass it by editing their
/// own crontab. Counting what the server actually holds makes the limit true rather than nearly
/// true — and it costs one extra agent call before every creation, which is the price of not
/// keeping a second copy of somebody else's data.
/// </para>
/// <para>
/// Order: account first, listing second, creation third. Spec §8 requires countable limits to be
/// enforced before the agent is asked to make anything, and here the listing is itself an agent
/// call — so the ordering that matters is that nothing is INSTALLED until the count has been read.
/// A listing that fails refuses the creation rather than being read as "zero entries so far", which
/// would turn an agent outage into an unlimited plan.
/// </para>
/// <para>
/// KNOWN RACE, deliberately not solved, exactly as the Sites, Databases and Sftp modules record:
/// this is count-then-create with no lock, so two concurrent creations can both read N. Being one
/// entry over a plan limit is a billing discrepancy an operator can see and correct, not a tenancy
/// or availability failure — and no lock the panel could take would cover the customer editing
/// their own crontab anyway.
/// </para>
/// <para>
/// Every failure is journalled and none of them carries the command (RULING 31): the subject is the
/// new entry's id on success and the ACCOUNT's id on refusal, because before the agent answers
/// there is no entry to name. See <see cref="CronAuditJournal"/>.
/// </para>
/// </remarks>
public sealed class CreateCronEntryCommandHandler
{
    /// <summary>The one window onto the owning account's system user name and plan allowance.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns the crontab — this module's only store.</summary>
    private readonly IAgentCronClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly CronAuditJournal _journal;

    /// <summary>Where an agent refusal leaves its code and its subject, and nothing else.</summary>
    private readonly ILogger<CreateCronEntryCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="accounts">The owning account's system user name and plan allowance.</param>
    /// <param name="agent">The agent client that reads and writes the crontab.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="logger">Where an agent refusal is reported, by code and entry id only.</param>
    public CreateCronEntryCommandHandler(
        IAccountDirectory accounts,
        IAgentCronClient agent,
        CronAuditJournal journal,
        ILogger<CreateCronEntryCommandHandler> logger)
    {
        _accounts = accounts;
        _agent = agent;
        _journal = journal;
        _logger = logger;
    }

    /// <summary>Installs the entry, refusing it before the crontab is touched when anything says no.</summary>
    /// <param name="command">The validated parameters; see <see cref="CreateCronEntryCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The installed entry — or <c>AccountNotFound</c>, <c>CronEntryLimitReached</c>,
    /// <c>CronEntryAlreadyExists</c> when the agent already holds this exact schedule and command,
    /// or <c>CronOperationFailed</c>.
    /// </returns>
    public async Task<Result<CronEntryDto>> HandleAsync(
        CreateCronEntryCommand command,
        CancellationToken cancellationToken)
    {
        // Tenant-scoped: the directory answers null for an account this caller does not own, so a
        // guessed account id reads as "not found" rather than "forbidden". This resolution is the
        // whole of the tenant boundary in a module that keeps no rows.
        var account = await _accounts.FindAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(command, Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound), cancellationToken);
        }

        var installed = await _agent.ListEntriesAsync(account.Username, cancellationToken);
        if (!installed.IsSuccess)
        {
            return await FailAsync(
                command,
                CronAgentErrorTranslator.Translate(
                    _logger, installed.Error!, nameof(_agent.ListEntriesAsync), Subject(command)),
                cancellationToken);
        }

        if (installed.Value.Count >= account.MaxCronEntries)
        {
            return await FailAsync(command, Error.Of(nameof(ErrorMessages.CronEntryLimitReached), ErrorType.Conflict), cancellationToken);
        }

        var created = await _agent.CreateEntryAsync(
            account.Username,
            CronScheduleTranslator.ToAgentSchedule(command.Schedule),
            command.Command,
            cancellationToken);
        if (!created.IsSuccess)
        {
            return await FailAsync(
                command,
                CronAgentErrorTranslator.Translate(
                    _logger, created.Error!, nameof(_agent.CreateEntryAsync), Subject(command)),
                cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            AuditActions.CronEntryCreated, created.Value, command.IpAddress, command.UserAgent, cancellationToken);

        // Enabled, because the agent installs a new entry as a live crontab line; disabling is a
        // separate operation and a separate audit action.
        return Result<CronEntryDto>.Ok(new CronEntryDto(
            created.Value, command.AccountId, command.Schedule, command.Command, Enabled: true));
    }

    /// <summary>The identifier a refused creation is recorded and logged against.</summary>
    /// <param name="command">The creation being refused.</param>
    /// <returns>The account's id.</returns>
    /// <remarks>
    /// The account rather than the entry, because a creation that has not reached the agent has no
    /// entry id to name — and the alternatives are worse. The command is forbidden outright
    /// (RULING 31, <see cref="CronAuditJournal"/>); an empty subject would leave a journal row that
    /// says an operation was refused without saying against what. The account id is exactly what a
    /// creation is attempted against, and it is an identifier rather than customer text.
    /// </remarks>
    private static string Subject(CreateCronEntryCommand command)
    {
        return command.AccountId.ToString();
    }

    /// <summary>Journals a refused creation and returns it as the typed failure.</summary>
    /// <param name="command">The creation that was refused.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<CronEntryDto>> FailAsync(
        CreateCronEntryCommand command,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AuditActions.CronEntryCreated, Subject(command), command.IpAddress, command.UserAgent, cancellationToken);

        return Result<CronEntryDto>.Fail(error);
    }
}
