using Maran.Agent.Client.Interfaces;
using Maran.Modules.Cron.Common;
using Maran.Modules.Cron.Mappers;
using Maran.Modules.Cron.Resources;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Cron.Queries.GetCronEntryOutput;

/// <summary>
/// Handles <see cref="GetCronEntryOutputQuery"/> by asking the agent what the entry's last run left
/// behind.
/// </summary>
/// <remarks>
/// A null answer means the entry has never run, and it is passed through as null rather than being
/// flattened into an empty reading. The three fields all have meaningful defaults — an empty string
/// is a run that printed nothing, zero is a successful exit, and zero seconds is the epoch — so any
/// invented value would tell a customer their job ran when it never has, which is precisely the
/// question somebody debugging a job that never fires is asking.
/// </remarks>
public sealed class GetCronEntryOutputQueryHandler
{
    /// <summary>The one window onto the owning account's system user name.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns the crontab — this module's only store.</summary>
    private readonly IAgentCronClient _agent;

    /// <summary>Where an agent refusal leaves its code and the entry id, and nothing else.</summary>
    private readonly ILogger<GetCronEntryOutputQueryHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="accounts">The owning account's system user name.</param>
    /// <param name="agent">The agent client that reads the run's leavings.</param>
    /// <param name="logger">Where an agent refusal is reported, by code and entry id only.</param>
    public GetCronEntryOutputQueryHandler(
        IAccountDirectory accounts,
        IAgentCronClient agent,
        ILogger<GetCronEntryOutputQueryHandler> logger)
    {
        _accounts = accounts;
        _agent = agent;
        _logger = logger;
    }

    /// <summary>Returns the last run's leavings, or nothing at all when the entry has never run.</summary>
    /// <param name="query">Which entry to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The reading, or null for an entry that has never run — or <c>AccountNotFound</c>,
    /// <c>CronEntryNotFound</c>, or <c>CronOperationFailed</c>.
    /// </returns>
    public async Task<Result<CronEntryOutputDto?>> HandleAsync(
        GetCronEntryOutputQuery query,
        CancellationToken cancellationToken)
    {
        var account = await _accounts.FindAsync(query.AccountId, cancellationToken);
        if (account is null)
        {
            return Result<CronEntryOutputDto?>.Fail(Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound));
        }

        var output = await _agent.GetEntryOutputAsync(account.Username, query.EntryId, cancellationToken);
        if (!output.IsSuccess)
        {
            return Result<CronEntryOutputDto?>.Fail(CronAgentErrorTranslator.Translate(
                _logger, output.Error!, nameof(_agent.GetEntryOutputAsync), query.EntryId));
        }

        if (output.Value is null)
        {
            return Result<CronEntryOutputDto?>.Ok(null);
        }

        return Result<CronEntryOutputDto?>.Ok(new CronEntryOutputDto(
            query.EntryId,
            output.Value.Output,
            output.Value.LastExitCode,
            output.Value.LastRunAtUnix));
    }
}
