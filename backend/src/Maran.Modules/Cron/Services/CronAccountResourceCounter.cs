using Maran.Agent.Client.Interfaces;
using Maran.Modules.Cron.Mappers;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Cron.Services;

/// <summary>
/// This module's answer to <see cref="IAccountResourceCounter"/>: how many entries the account's
/// crontab holds, as the agent reports it.
/// </summary>
/// <remarks>
/// <para>
/// Counted against the agent's listing for the reason <c>CreateCronEntryCommandHandler</c> gives:
/// the crontab is the record and this module keeps no rows, so the count the summary shows is the
/// same count the plan limit is enforced against.
/// </para>
/// <para>
/// An agent that does not answer makes the count <see cref="AccountResourceCountState.Unavailable"/>
/// rather than failing the whole summary or reading as zero entries: the other kinds were still
/// counted, and zero would be a claim about a crontab nobody read. An account the caller's scope
/// cannot resolve answers the same way — there is no crontab this caller may read.
/// </para>
/// </remarks>
public sealed class CronAccountResourceCounter : IAccountResourceCounter
{
    /// <summary>The window onto the account's system user name, tenant-scoped.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns the crontab.</summary>
    private readonly IAgentCronClient _agent;

    /// <summary>Where an agent refusal is reported, by code and account id only.</summary>
    private readonly ILogger<CronAccountResourceCounter> _logger;

    /// <summary>Creates the counter.</summary>
    /// <param name="accounts">The account directory resolving the system user name.</param>
    /// <param name="agent">The agent client that lists the crontab.</param>
    /// <param name="logger">Where an agent refusal is reported.</param>
    public CronAccountResourceCounter(
        IAccountDirectory accounts,
        IAgentCronClient agent,
        ILogger<CronAccountResourceCounter> logger)
    {
        _accounts = accounts;
        _agent = agent;
        _logger = logger;
    }

    /// <inheritdoc />
    public AccountResourceKind Kind
    {
        get
        {
            return AccountResourceKind.CronEntries;
        }
    }

    /// <inheritdoc />
    public async Task<AccountResourceCount> CountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await _accounts.FindAsync(accountId, cancellationToken);
        if (account is null)
        {
            return AccountResourceCount.Unavailable(AccountResourceKind.CronEntries);
        }

        var installed = await _agent.ListEntriesAsync(account.Username, cancellationToken);
        if (!installed.IsSuccess)
        {
            // Translated only for its log line: the summary carries no error, only the state.
            CronAgentErrorTranslator.Translate(
                _logger, installed.Error!, nameof(_agent.ListEntriesAsync), accountId.ToString());
            return AccountResourceCount.Unavailable(AccountResourceKind.CronEntries);
        }

        return AccountResourceCount.Counted(AccountResourceKind.CronEntries, installed.Value.Count);
    }
}
