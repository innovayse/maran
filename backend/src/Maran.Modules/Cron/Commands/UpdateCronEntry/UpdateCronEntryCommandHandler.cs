using Maran.Agent.Client.Interfaces;
using Maran.Modules.Cron.Mappers;
using Maran.Modules.Cron.Resources;
using Maran.Modules.Cron.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Cron.Commands.UpdateCronEntry;

/// <summary>
/// Handles <see cref="UpdateCronEntryCommand"/>: resolves the account and rewrites the entry through
/// the agent.
/// </summary>
/// <remarks>
/// <para>
/// No plan limit is consulted, and that is not an omission: an update replaces an entry that is
/// already installed, so it cannot take an account past an allowance it is already inside. Checking
/// one here would mean an account at its limit could no longer FIX a job — which is the moment they
/// most need to.
/// </para>
/// <para>
/// An entry this account does not have is answered <c>CronEntryNotFound</c>, which is a 404 — never a
/// 403. The panel cannot tell "no such entry" from "somebody else's entry" and must not: the two
/// answers differing is the oracle. It cannot tell them apart for a structural reason rather than a
/// chosen one — the agent is asked for the entry UNDER THIS ACCOUNT'S CRONTAB, so another tenant's
/// entry id simply is not there.
/// </para>
/// </remarks>
public sealed class UpdateCronEntryCommandHandler
{
    /// <summary>The one window onto the owning account's system user name.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns the crontab — this module's only store.</summary>
    private readonly IAgentCronClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly CronAuditJournal _journal;

    /// <summary>Where an agent refusal leaves its code and the entry id, and nothing else.</summary>
    private readonly ILogger<UpdateCronEntryCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="accounts">The owning account's system user name.</param>
    /// <param name="agent">The agent client that rewrites the entry.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="logger">Where an agent refusal is reported, by code and entry id only.</param>
    public UpdateCronEntryCommandHandler(
        IAccountDirectory accounts,
        IAgentCronClient agent,
        CronAuditJournal journal,
        ILogger<UpdateCronEntryCommandHandler> logger)
    {
        _accounts = accounts;
        _agent = agent;
        _journal = journal;
        _logger = logger;
    }

    /// <summary>Rewrites the entry's schedule and command, leaving its enablement alone.</summary>
    /// <param name="command">The validated parameters; see <see cref="UpdateCronEntryCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// Success — or <c>AccountNotFound</c>, <c>CronEntryNotFound</c>, or <c>CronOperationFailed</c>.
    /// </returns>
    /// <remarks>
    /// It answers a bare success rather than the rewritten entry, which is the honest shape: this
    /// operation deliberately does not read the entry's enablement and must not change it, so an
    /// entry echoed back here could only guess at that flag — and a guess of "enabled" would tell a
    /// customer their disabled job is live again. The listing is the one thing that knows.
    /// </remarks>
    public async Task<Result<bool>> HandleAsync(
        UpdateCronEntryCommand command,
        CancellationToken cancellationToken)
    {
        var account = await _accounts.FindAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(command, Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound), cancellationToken);
        }

        var updated = await _agent.UpdateEntryAsync(
            account.Username,
            command.EntryId,
            CronScheduleTranslator.ToAgentSchedule(command.Schedule),
            command.Command,
            cancellationToken);
        if (!updated.IsSuccess)
        {
            return await FailAsync(
                command,
                CronAgentErrorTranslator.Translate(
                    _logger, updated.Error!, nameof(_agent.UpdateEntryAsync), command.EntryId),
                cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            AuditActions.CronEntryUpdated,
            command.EntryId,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<bool>.Ok(true);
    }

    /// <summary>Journals a refused update and returns it as the typed failure.</summary>
    /// <param name="command">The update that was refused; its entry id is the journal's subject.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<bool>> FailAsync(
        UpdateCronEntryCommand command,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AuditActions.CronEntryUpdated,
            command.EntryId,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<bool>.Fail(error);
    }
}
