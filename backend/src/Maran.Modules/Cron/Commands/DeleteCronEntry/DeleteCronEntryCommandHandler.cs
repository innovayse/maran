using Maran.Agent.Client.Interfaces;
using Maran.Modules.Cron.Mappers;
using Maran.Modules.Cron.Resources;
using Maran.Modules.Cron.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Cron.Commands.DeleteCronEntry;

/// <summary>
/// Handles <see cref="DeleteCronEntryCommand"/>: resolves the account and removes the entry through
/// the agent.
/// </summary>
/// <remarks>
/// There is no row to remove afterwards, and therefore none of the ordering problems the modules
/// that keep rows have to reason about. The crontab is the record, so the agent's answer is the
/// whole outcome — a removal that succeeds is complete, and one that fails has changed nothing.
///
/// An entry this account does not have is answered <c>CronEntryNotFound</c> — 404, never 403, and
/// the same answer another tenant's entry id gets. The agent is asked for the entry under THIS
/// account's crontab, so another tenant's entry is not there to be found; the indistinguishability
/// is structural rather than a check somebody has to remember to write.
/// </remarks>
public sealed class DeleteCronEntryCommandHandler
{
    /// <summary>The one window onto the owning account's system user name.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns the crontab — this module's only store.</summary>
    private readonly IAgentCronClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly CronAuditJournal _journal;

    /// <summary>Where an agent refusal leaves its code and the entry id, and nothing else.</summary>
    private readonly ILogger<DeleteCronEntryCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="accounts">The owning account's system user name.</param>
    /// <param name="agent">The agent client that removes the entry.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="logger">Where an agent refusal is reported, by code and entry id only.</param>
    public DeleteCronEntryCommandHandler(
        IAccountDirectory accounts,
        IAgentCronClient agent,
        CronAuditJournal journal,
        ILogger<DeleteCronEntryCommandHandler> logger)
    {
        _accounts = accounts;
        _agent = agent;
        _journal = journal;
        _logger = logger;
    }

    /// <summary>Removes the entry. A second attempt is answered not found.</summary>
    /// <param name="command">The validated parameters; see <see cref="DeleteCronEntryCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// Success — or <c>AccountNotFound</c>, <c>CronEntryNotFound</c>, or <c>CronOperationFailed</c>.
    /// </returns>
    public async Task<Result<bool>> HandleAsync(
        DeleteCronEntryCommand command,
        CancellationToken cancellationToken)
    {
        var account = await _accounts.FindAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(command, Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound), cancellationToken);
        }

        var deleted = await _agent.DeleteEntryAsync(account.Username, command.EntryId, cancellationToken);
        if (!deleted.IsSuccess)
        {
            return await FailAsync(
                command,
                CronAgentErrorTranslator.Translate(
                    _logger, deleted.Error!, nameof(_agent.DeleteEntryAsync), command.EntryId),
                cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            AuditActions.CronEntryDeleted,
            command.EntryId,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<bool>.Ok(true);
    }

    /// <summary>Journals a refused removal and returns it as the typed failure.</summary>
    /// <param name="command">The removal that was refused; its entry id is the journal's subject.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<bool>> FailAsync(
        DeleteCronEntryCommand command,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AuditActions.CronEntryDeleted,
            command.EntryId,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<bool>.Fail(error);
    }
}
