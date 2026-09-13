using Maran.Agent.Client.Interfaces;
using Maran.Modules.Cron.Common;
using Maran.Modules.Cron.Mappers;
using Maran.Modules.Cron.Resources;
using Maran.Modules.Cron.Services;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Cron.Queries.GetCronEnvironment;

/// <summary>
/// Handles <see cref="GetCronEnvironmentQuery"/> by asking the agent what the managed region of the
/// account's crontab holds.
/// </summary>
/// <remarks>
/// The values come back in full, because they are the customer's own and this response goes to
/// them — the same distinction the command carries (<see cref="CronEntryDto.Command"/>). They reach
/// no log line and no audit row on the way (<see cref="CronAuditJournal"/>).
/// </remarks>
public sealed class GetCronEnvironmentQueryHandler
{
    /// <summary>The one window onto the owning account's system user name.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns the crontab — this module's only store.</summary>
    private readonly IAgentCronClient _agent;

    /// <summary>Where an agent refusal leaves its code and the account id, and nothing else.</summary>
    private readonly ILogger<GetCronEnvironmentQueryHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="accounts">The owning account's system user name.</param>
    /// <param name="agent">The agent client that reads the assignments.</param>
    /// <param name="logger">Where an agent refusal is reported, by code and account id only.</param>
    public GetCronEnvironmentQueryHandler(
        IAccountDirectory accounts,
        IAgentCronClient agent,
        ILogger<GetCronEnvironmentQueryHandler> logger)
    {
        _accounts = accounts;
        _agent = agent;
        _logger = logger;
    }

    /// <summary>Returns the managed assignments in the order the crontab holds them.</summary>
    /// <param name="query">Which account's crontab to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The assignments — or <c>AccountNotFound</c>, or <c>CronOperationFailed</c>.</returns>
    public async Task<Result<IReadOnlyList<CronEnvironmentVariableDto>>> HandleAsync(
        GetCronEnvironmentQuery query,
        CancellationToken cancellationToken)
    {
        var account = await _accounts.FindAsync(query.AccountId, cancellationToken);
        if (account is null)
        {
            return Result<IReadOnlyList<CronEnvironmentVariableDto>>.Fail(
                Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound));
        }

        var variables = await _agent.GetEnvironmentAsync(account.Username, cancellationToken);
        if (!variables.IsSuccess)
        {
            return Result<IReadOnlyList<CronEnvironmentVariableDto>>.Fail(CronAgentErrorTranslator.Translate(
                _logger, variables.Error!, nameof(_agent.GetEnvironmentAsync), query.AccountId.ToString()));
        }

        var assignments = variables.Value
            .Select(variable =>
            {
                return new CronEnvironmentVariableDto(variable.Name, variable.Value);
            })
            .ToList();

        return Result<IReadOnlyList<CronEnvironmentVariableDto>>.Ok(assignments);
    }
}
