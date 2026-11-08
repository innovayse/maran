using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.CronService;
using Maran.Agent.Simulated.State;

namespace Maran.Agent.Simulated.Services.CronService;

/// <summary>
/// The agent's cron operations against the simulated server's memory: entries are stored and listed,
/// and a run by hand returns a fabricated result without starting anything.
/// </summary>
/// <remarks>
/// The command a visitor types is never interpreted. Nothing here parses it, expands it or hands it
/// to an interpreter; it is a string that comes back when the entry is listed.
/// </remarks>
public sealed class SimulatedAgentCronClient : IAgentCronClient
{
    /// <summary>The crontabs of the simulated server.</summary>
    private readonly SimulatedCronStore _cron;

    /// <summary>The clock a recorded run takes its time from.</summary>
    private readonly IClock _clock;

    /// <summary>Creates a client over the shared simulated state.</summary>
    /// <param name="ledger">The simulated server's state.</param>
    /// <param name="clock">The clock a run by hand is stamped with.</param>
    public SimulatedAgentCronClient(SimulatedAgentLedger ledger, IClock clock)
    {
        _cron = ledger.CronTables;
        _clock = clock;
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<AgentCronEntry>>> ListEntriesAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<IReadOnlyList<AgentCronEntry>>.Ok(_cron.List(accountUsername)));
    }

    /// <inheritdoc/>
    public Task<Result<string>> CreateEntryAsync(
        string accountUsername,
        AgentCronSchedule schedule,
        string command,
        uint? maxEntries,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_cron.Create(accountUsername, schedule, command, maxEntries));
    }

    /// <inheritdoc/>
    public Task<Result> UpdateEntryAsync(
        string accountUsername,
        string entryId,
        AgentCronSchedule schedule,
        string command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_cron.Update(accountUsername, entryId, schedule, command));
    }

    /// <inheritdoc/>
    public Task<Result> SetAccountSuspendedAsync(
        string accountUsername,
        bool suspended,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _cron.SetSuspended(accountUsername, suspended);

        return Task.FromResult(Result.Ok());
    }

    /// <inheritdoc/>
    public Task<Result> DeleteEntryAsync(string accountUsername, string entryId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_cron.Delete(accountUsername, entryId));
    }

    /// <inheritdoc/>
    public Task<Result> SetEntryEnabledAsync(
        string accountUsername,
        string entryId,
        bool enabled,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_cron.SetEnabled(accountUsername, entryId, enabled));
    }

    /// <inheritdoc/>
    public Task<Result<AgentCronRunOutput?>> GetEntryOutputAsync(
        string accountUsername,
        string entryId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_cron.GetOutput(accountUsername, entryId));
    }

    /// <inheritdoc/>
    public Task<Result<AgentCronRun>> RunEntryAsync(
        string accountUsername,
        string entryId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_cron.Run(accountUsername, entryId, _clock.UtcNow));
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<AgentCronEnvVar>>> GetEnvironmentAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<IReadOnlyList<AgentCronEnvVar>>.Ok(_cron.GetEnvironment(accountUsername)));
    }

    /// <inheritdoc/>
    public Task<Result> SetEnvironmentAsync(
        string accountUsername,
        IReadOnlyList<AgentCronEnvVar> variables,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_cron.SetEnvironment(accountUsername, variables));
    }
}
