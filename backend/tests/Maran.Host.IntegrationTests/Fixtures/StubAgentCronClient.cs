using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.CronService;
using Maran.SharedKernel.Results;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// Stands in for the agent while the panel's own cron path is exercised end to end: the real
/// controllers, the real account resolution, the real validators and the real result translation,
/// over real HTTP and real PostgreSQL.
/// </summary>
/// <remarks>
/// Only the agent is replaced, and only because it cannot be present: it is a separate root process
/// that edits crontabs on a provisioned host. Everything between the HTTP request and this boundary
/// is the shipped implementation, so a test that passes here is not merely a test that a call was
/// made (rules/testing.md).
///
/// It stands in for more than a transport in this module. The Cron module keeps no rows, so this
/// double IS the crontab as far as the panel can tell — which is why what it RECORDS matters as much
/// as what it answers: the account user names it was addressed by are the evidence that no request
/// reached another tenant's crontab.
///
/// It is deliberately dumb — it replays a fixed script and records what it was asked. It asserts
/// nothing itself; the tests do.
/// </remarks>
public sealed class StubAgentCronClient : IAgentCronClient
{
    /// <summary>The identifier this double reports for the one entry it pretends to hold.</summary>
    public const string EntryId = "3f1a5b7c-0d2e-4a6b-8c9d-0e1f2a3b4c5d";

    /// <summary>Every account user name this client was addressed by, in order, across every call.</summary>
    /// <remarks>
    /// One list rather than one per operation, because the question the tests ask of it is the same
    /// for all of them: was any crontab but the signed-in customer's ever touched.
    /// </remarks>
    public List<string> AddressedAccounts { get; } = [];

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<AgentCronEntry>>> ListEntriesAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        AddressedAccounts.Add(accountUsername);

        return Task.FromResult(Result<IReadOnlyList<AgentCronEntry>>.Ok(
        [
            new AgentCronEntry(
                EntryId,
                new AgentCronSchedule("0", "3", "*", "*", "*"),
                $"/usr/bin/backup --account {accountUsername}",
                true),
        ]));
    }

    /// <inheritdoc/>
    public Task<Result<string>> CreateEntryAsync(
        string accountUsername,
        AgentCronSchedule schedule,
        string command,
        CancellationToken cancellationToken)
    {
        AddressedAccounts.Add(accountUsername);

        return Task.FromResult(Result<string>.Ok(EntryId));
    }

    /// <inheritdoc/>
    public Task<Result<bool>> UpdateEntryAsync(
        string accountUsername,
        string entryId,
        AgentCronSchedule schedule,
        string command,
        CancellationToken cancellationToken)
    {
        AddressedAccounts.Add(accountUsername);

        return Task.FromResult(Result<bool>.Ok(true));
    }

    /// <inheritdoc/>
    public Task<Result<bool>> DeleteEntryAsync(
        string accountUsername,
        string entryId,
        CancellationToken cancellationToken)
    {
        AddressedAccounts.Add(accountUsername);

        return Task.FromResult(Result<bool>.Ok(true));
    }

    /// <inheritdoc/>
    public Task<Result<bool>> SetEntryEnabledAsync(
        string accountUsername,
        string entryId,
        bool enabled,
        CancellationToken cancellationToken)
    {
        AddressedAccounts.Add(accountUsername);

        return Task.FromResult(Result<bool>.Ok(true));
    }

    /// <inheritdoc/>
    public Task<Result<AgentCronRunOutput?>> GetEntryOutputAsync(
        string accountUsername,
        string entryId,
        CancellationToken cancellationToken)
    {
        AddressedAccounts.Add(accountUsername);

        return Task.FromResult(Result<AgentCronRunOutput?>.Ok(
            new AgentCronRunOutput("done", 0, 1_772_000_000)));
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<AgentCronEnvVar>>> GetEnvironmentAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        AddressedAccounts.Add(accountUsername);

        return Task.FromResult(Result<IReadOnlyList<AgentCronEnvVar>>.Ok(
            [new AgentCronEnvVar("TZ", accountUsername)]));
    }

    /// <inheritdoc/>
    public Task<Result<bool>> SetEnvironmentAsync(
        string accountUsername,
        IReadOnlyList<AgentCronEnvVar> variables,
        CancellationToken cancellationToken)
    {
        AddressedAccounts.Add(accountUsername);

        return Task.FromResult(Result<bool>.Ok(true));
    }
}
