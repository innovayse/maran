using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.CronService;
using Maran.Modules.Cron.Mappers;
using Maran.Modules.Cron.Resources;
using Maran.Modules.Cron.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Cron.Commands.SetCronEnvironment;

/// <summary>
/// Handles <see cref="SetCronEnvironmentCommand"/>: resolves the account and replaces the managed
/// environment assignments through the agent.
/// </summary>
/// <remarks>
/// The journal records the NAMES that were set and never the values. A name is what an operator
/// needs — "somebody changed <c>DATABASE_URL</c> on this account at 03:12" answers the question a
/// broken job raises — and a value is a credential in an append-only journal that is never deleted
/// (<see cref="CronAuditJournal"/>).
/// </remarks>
public sealed class SetCronEnvironmentCommandHandler
{
    /// <summary>The most characters the audit journal stores for one entry's subject.</summary>
    /// <remarks>
    /// The journal's own column is bounded, and a set of long names could otherwise compose a
    /// subject past it — which would fail the WRITE, so a legitimate change would be refused by its
    /// own audit trail. The names are therefore truncated to fit rather than allowed to overflow: a
    /// shortened list still names the change, while a failed write records nothing at all.
    /// </remarks>
    private const int MaximumSubjectLength = 256;

    /// <summary>What marks a name list that did not fit.</summary>
    private const string TruncationMarker = "…";

    /// <summary>The subject recorded when the new set is empty.</summary>
    /// <remarks>
    /// Clearing every assignment is a real request the agent honours, and the journal must say so.
    /// An empty subject would be a row that records an operation against nothing.
    /// </remarks>
    private const string ClearedSubject = "(cleared)";

    /// <summary>The one window onto the owning account's system user name.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns the crontab — this module's only store.</summary>
    private readonly IAgentCronClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly CronAuditJournal _journal;

    /// <summary>Where an agent refusal leaves its code and the account id, and nothing else.</summary>
    private readonly ILogger<SetCronEnvironmentCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="accounts">The owning account's system user name.</param>
    /// <param name="agent">The agent client that rewrites the assignments.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="logger">Where an agent refusal is reported, by code and account id only.</param>
    public SetCronEnvironmentCommandHandler(
        IAccountDirectory accounts,
        IAgentCronClient agent,
        CronAuditJournal journal,
        ILogger<SetCronEnvironmentCommandHandler> logger)
    {
        _accounts = accounts;
        _agent = agent;
        _journal = journal;
        _logger = logger;
    }

    /// <summary>Replaces the managed assignments with exactly the set the caller sent.</summary>
    /// <param name="command">The validated parameters; see <see cref="SetCronEnvironmentCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success — or <c>AccountNotFound</c>, or <c>CronOperationFailed</c>.</returns>
    public async Task<Result<bool>> HandleAsync(
        SetCronEnvironmentCommand command,
        CancellationToken cancellationToken)
    {
        var account = await _accounts.FindAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(command, Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound), cancellationToken);
        }

        var variables = command.Variables
            .Select(variable =>
            {
                return new AgentCronEnvVar(variable.Name, variable.Value);
            })
            .ToList();

        var applied = await _agent.SetEnvironmentAsync(account.Username, variables, cancellationToken);
        if (!applied.IsSuccess)
        {
            return await FailAsync(
                command,
                CronAgentErrorTranslator.Translate(
                    _logger,
                    applied.Error!,
                    nameof(_agent.SetEnvironmentAsync),
                    command.AccountId.ToString()),
                cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            AuditActions.CronEnvironmentChanged,
            Subject(command),
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<bool>.Ok(true);
    }

    /// <summary>Builds the journal subject: the names that were set, bounded, and never their values.</summary>
    /// <param name="command">The change being recorded.</param>
    /// <returns>The comma-separated names, truncated to what the journal stores.</returns>
    private static string Subject(SetCronEnvironmentCommand command)
    {
        if (command.Variables.Count == 0)
        {
            return ClearedSubject;
        }

        var names = string.Join(
            ',',
            command.Variables.Select(variable =>
            {
                return variable.Name;
            }));

        return names.Length <= MaximumSubjectLength
            ? names
            : string.Concat(names.AsSpan(0, MaximumSubjectLength - TruncationMarker.Length), TruncationMarker);
    }

    /// <summary>Journals a refused change and returns it as the typed failure.</summary>
    /// <param name="command">The change that was refused.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<bool>> FailAsync(
        SetCronEnvironmentCommand command,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AuditActions.CronEnvironmentChanged,
            Subject(command),
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<bool>.Fail(error);
    }
}
