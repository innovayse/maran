using System.Net.Sockets;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.MonitorService;
using Maran.SharedKernel.Results;

namespace Maran.Host.Tests.Resilience;

/// <summary>An inner monitoring client that counts its calls and can fail or hang on demand.</summary>
/// <remarks>
/// Hanging is as important as failing here, and more so than anywhere else: every method on this
/// contract is a read, reads are the calls most easily waved through as harmless, and a dashboard
/// polls them. Only a call that never returns proves the decorator has a TIMEOUT.
/// </remarks>
internal sealed class RecordingAgentMonitorClient : IAgentMonitorClient
{
    /// <summary>How many calls fail with a transport error before one succeeds.</summary>
    public int FailuresBeforeSuccess { get; set; }

    /// <summary>When true, every call waits for its cancellation token instead of returning.</summary>
    public bool Hangs { get; set; }

    /// <summary>How many times any method on this client was entered.</summary>
    public int Calls { get; private set; }

    /// <inheritdoc/>
    public async Task<Result<AgentHostMetrics>> GetHostMetricsAsync(CancellationToken cancellationToken)
    {
        await EnterAsync(cancellationToken);

        return Result<AgentHostMetrics>.Ok(new AgentHostMetrics(0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<AgentServiceStatus>>> GetServiceStatusesAsync(
        CancellationToken cancellationToken)
    {
        await EnterAsync(cancellationToken);

        return Result<IReadOnlyList<AgentServiceStatus>>.Ok([]);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<AgentAccountDiskUsage>>> GetAccountsDiskUsageAsync(
        CancellationToken cancellationToken)
    {
        await EnterAsync(cancellationToken);

        return Result<IReadOnlyList<AgentAccountDiskUsage>>.Ok([]);
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
