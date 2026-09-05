using System.Net.Sockets;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.CronService;
using Maran.SharedKernel.Results;

namespace Maran.Host.Tests.Resilience;

/// <summary>An inner cron client that records its arguments and can fail or hang on demand.</summary>
/// <remarks>
/// Hanging is as important as failing here. A retry proves the call passed through something; only a
/// call that never returns proves the something has a TIMEOUT, which is the whole reason the
/// decorator exists — a stuck unix socket must not hang the HTTP request that made the call.
/// </remarks>
internal sealed class RecordingAgentCronClient : IAgentCronClient
{
    /// <summary>How many calls fail with a transport error before one succeeds.</summary>
    public int FailuresBeforeSuccess { get; set; }

    /// <summary>When true, every call waits for its cancellation token instead of returning.</summary>
    public bool Hangs { get; set; }

    /// <summary>How many times any method on this client was entered.</summary>
    public int Calls { get; private set; }

    /// <summary>The account username of the last call.</summary>
    public string? LastAccountUsername { get; private set; }

    /// <summary>The entry identifier of the last call that named one.</summary>
    public string? LastEntryId { get; private set; }

    /// <summary>The schedule of the last call that carried one.</summary>
    public AgentCronSchedule? LastSchedule { get; private set; }

    /// <summary>The command of the last call that carried one.</summary>
    public string? LastCommand { get; private set; }

    /// <summary>The enablement of the last call that carried one.</summary>
    public bool? LastEnabled { get; private set; }

    /// <summary>The environment of the last call that carried one.</summary>
    public IReadOnlyList<AgentCronEnvVar>? LastVariables { get; private set; }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<AgentCronEntry>>> ListEntriesAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        LastAccountUsername = accountUsername;

        await EnterAsync(cancellationToken);

        return Result<IReadOnlyList<AgentCronEntry>>.Ok([]);
    }

    /// <inheritdoc/>
    public async Task<Result<string>> CreateEntryAsync(
        string accountUsername,
        AgentCronSchedule schedule,
        string command,
        CancellationToken cancellationToken)
    {
        LastAccountUsername = accountUsername;
        LastSchedule = schedule;
        LastCommand = command;

        await EnterAsync(cancellationToken);

        return Result<string>.Ok("e1");
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> UpdateEntryAsync(
        string accountUsername,
        string entryId,
        AgentCronSchedule schedule,
        string command,
        CancellationToken cancellationToken)
    {
        LastAccountUsername = accountUsername;
        LastEntryId = entryId;
        LastSchedule = schedule;
        LastCommand = command;

        await EnterAsync(cancellationToken);

        return Result<bool>.Ok(true);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> DeleteEntryAsync(
        string accountUsername,
        string entryId,
        CancellationToken cancellationToken)
    {
        LastAccountUsername = accountUsername;
        LastEntryId = entryId;

        await EnterAsync(cancellationToken);

        return Result<bool>.Ok(true);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> SetEntryEnabledAsync(
        string accountUsername,
        string entryId,
        bool enabled,
        CancellationToken cancellationToken)
    {
        LastAccountUsername = accountUsername;
        LastEntryId = entryId;
        LastEnabled = enabled;

        await EnterAsync(cancellationToken);

        return Result<bool>.Ok(true);
    }

    /// <inheritdoc/>
    public async Task<Result<AgentCronRunOutput?>> GetEntryOutputAsync(
        string accountUsername,
        string entryId,
        CancellationToken cancellationToken)
    {
        LastAccountUsername = accountUsername;
        LastEntryId = entryId;

        await EnterAsync(cancellationToken);

        return Result<AgentCronRunOutput?>.Ok(null);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<AgentCronEnvVar>>> GetEnvironmentAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        LastAccountUsername = accountUsername;

        await EnterAsync(cancellationToken);

        return Result<IReadOnlyList<AgentCronEnvVar>>.Ok([]);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> SetEnvironmentAsync(
        string accountUsername,
        IReadOnlyList<AgentCronEnvVar> variables,
        CancellationToken cancellationToken)
    {
        LastAccountUsername = accountUsername;
        LastVariables = variables;

        await EnterAsync(cancellationToken);

        return Result<bool>.Ok(true);
    }

    /// <summary>Counts the call and applies whichever misbehaviour the test asked for.</summary>
    /// <param name="cancellationToken">The token the pipeline's timeout cancels.</param>
    /// <returns>A task that completes once the call may return.</returns>
    private async Task EnterAsync(CancellationToken cancellationToken)
    {
        Calls++;

        if (Calls <= FailuresBeforeSuccess)
        {
            throw new SocketException((int)SocketError.ConnectionRefused);
        }

        if (Hangs)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        await Task.Yield();
    }
}
