using Maran.Agent.Client.Interfaces;
using Maran.Modules.Cron.Mappers;
using Maran.Modules.Cron.Resources;
using Maran.Modules.Cron.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Cron.Commands.SetCronEntryEnabled;

/// <summary>
/// Handles <see cref="SetCronEntryEnabledCommand"/>: resolves the account and switches the entry
/// through the agent.
/// </summary>
/// <remarks>
/// Enabling is not a creation and does not consult the plan limit. An entry that is already in the
/// crontab is already counted — a disabled one occupies a line and is returned by the listing the
/// creation path counts — so charging for it again here would refuse a customer at their limit the
/// ability to turn their own jobs back on.
///
/// An entry this account does not have is answered <c>CronEntryNotFound</c> — 404, never 403, and
/// the same answer another tenant's entry id gets.
/// </remarks>
public sealed class SetCronEntryEnabledCommandHandler
{
    /// <summary>The one window onto the owning account's system user name.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns the crontab — this module's only store.</summary>
    private readonly IAgentCronClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly CronAuditJournal _journal;

    /// <summary>Where an agent refusal leaves its code and the entry id, and nothing else.</summary>
    private readonly ILogger<SetCronEntryEnabledCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="accounts">The owning account's system user name.</param>
    /// <param name="agent">The agent client that switches the entry.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="logger">Where an agent refusal is reported, by code and entry id only.</param>
    public SetCronEntryEnabledCommandHandler(
        IAccountDirectory accounts,
        IAgentCronClient agent,
        CronAuditJournal journal,
        ILogger<SetCronEntryEnabledCommandHandler> logger)
    {
        _accounts = accounts;
        _agent = agent;
        _journal = journal;
        _logger = logger;
    }

    /// <summary>Switches the entry on or off, leaving its schedule and command alone.</summary>
    /// <param name="command">The validated parameters; see <see cref="SetCronEntryEnabledCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// Success — or <c>AccountNotFound</c>, <c>CronEntryNotFound</c>, or <c>CronOperationFailed</c>.
    /// </returns>
    public async Task<Result<bool>> HandleAsync(
        SetCronEntryEnabledCommand command,
        CancellationToken cancellationToken)
    {
        var account = await _accounts.FindAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(command, Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound), cancellationToken);
        }

        var switched = await _agent.SetEntryEnabledAsync(
            account.Username, command.EntryId, command.Enabled, cancellationToken);
        if (!switched.IsSuccess)
        {
            return await FailAsync(
                command,
                CronAgentErrorTranslator.Translate(
                    _logger, switched.Error!, nameof(_agent.SetEntryEnabledAsync), command.EntryId),
                cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            AuditActions.CronEntryEnabledChanged,
            command.EntryId,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<bool>.Ok(true);
    }

    /// <summary>Journals a refused switch and returns it as the typed failure.</summary>
    /// <param name="command">The switch that was refused; its entry id is the journal's subject.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<bool>> FailAsync(
        SetCronEntryEnabledCommand command,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AuditActions.CronEntryEnabledChanged,
            command.EntryId,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<bool>.Fail(error);
    }
}
