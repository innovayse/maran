using Maran.Agent.Client.Interfaces;
using Maran.Modules.Cron.Common;
using Maran.Modules.Cron.Mappers;
using Maran.Modules.Cron.Resources;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Cron.Queries.ListCronEntries;

/// <summary>
/// Handles <see cref="ListCronEntriesQuery"/> by asking the agent what the account's crontab holds.
/// </summary>
/// <remarks>
/// <para>
/// <b>The agent's listing IS the answer, and there is no panel copy to prefer to it.</b> That is the
/// opposite of the Databases and Sftp modules, which deliberately never list from the server —
/// because there the server's names carry no tenant and a prefix match would disclose one account's
/// databases to another. Here the agent is asked for ONE account's crontab, by that account's system
/// user name, so what comes back cannot belong to anybody else: the isolation is the operating
/// system's own, not a prefix the panel hopes nobody else shares.
/// </para>
/// <para>
/// A listing carries no exit status and no last-run time. Reading those means one privileged read
/// per entry, which would turn one listing into N of them, so the agent does not do it and this
/// handler does not invent zeros to fill the gap — "the last run succeeded, at the epoch" is a
/// measurement nobody made. <c>GetCronEntryOutput</c> answers that question for the entry being
/// looked at.
/// </para>
/// </remarks>
public sealed class ListCronEntriesQueryHandler
{
    /// <summary>The one window onto the owning account's system user name.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns the crontab — this module's only store.</summary>
    private readonly IAgentCronClient _agent;

    /// <summary>Where an agent refusal leaves its code and the account id, and nothing else.</summary>
    private readonly ILogger<ListCronEntriesQueryHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="accounts">The owning account's system user name.</param>
    /// <param name="agent">The agent client that reads the crontab.</param>
    /// <param name="logger">Where an agent refusal is reported, by code and account id only.</param>
    public ListCronEntriesQueryHandler(
        IAccountDirectory accounts,
        IAgentCronClient agent,
        ILogger<ListCronEntriesQueryHandler> logger)
    {
        _accounts = accounts;
        _agent = agent;
        _logger = logger;
    }

    /// <summary>Returns the account's entries in the order the crontab holds them.</summary>
    /// <param name="query">Which account's crontab to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The entries — or <c>AccountNotFound</c>, or <c>CronOperationFailed</c>.</returns>
    public async Task<Result<IReadOnlyList<CronEntryDto>>> HandleAsync(
        ListCronEntriesQuery query,
        CancellationToken cancellationToken)
    {
        // Reads are journalled by no module here, but they are tenant-scoped by all of them: an
        // account this caller does not own answers "not found", which is a 404 and never a 403.
        var account = await _accounts.FindAsync(query.AccountId, cancellationToken);
        if (account is null)
        {
            return Result<IReadOnlyList<CronEntryDto>>.Fail(Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound));
        }

        var entries = await _agent.ListEntriesAsync(account.Username, cancellationToken);
        if (!entries.IsSuccess)
        {
            return Result<IReadOnlyList<CronEntryDto>>.Fail(CronAgentErrorTranslator.Translate(
                _logger, entries.Error!, nameof(_agent.ListEntriesAsync), query.AccountId.ToString()));
        }

        var listing = entries.Value
            .Select(entry =>
            {
                return new CronEntryDto(
                    entry.EntryId,
                    query.AccountId,
                    CronScheduleTranslator.ToDto(entry.Schedule),
                    entry.Command,
                    entry.Enabled);
            })
            .ToList();

        return Result<IReadOnlyList<CronEntryDto>>.Ok(listing);
    }
}
