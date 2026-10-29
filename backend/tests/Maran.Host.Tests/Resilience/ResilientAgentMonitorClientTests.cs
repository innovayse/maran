using Maran.Host.Resilience;
using Maran.Host.Tests.Fixtures;
using Maran.SharedKernel.Results;

namespace Maran.Host.Tests.Resilience;

/// <summary>What the monitoring decorator does: every read goes through the pipeline.</summary>
/// <remarks>
/// Each method has its own retry test rather than one test standing for the class. A method that
/// forgets the pipeline is invisible from its call site and from every other method's test — this
/// repository has already shipped exactly that, one undecorated method inside a decorated class,
/// with the whole suite green.
///
/// Every method here is a read, which is the reason to be careful rather than a reason to relax: a
/// dashboard polls them, so an undecorated read against a wedged host holds a request open once per
/// poll, per viewer, for as long as the host stays wedged.
/// </remarks>
public sealed class ResilientAgentMonitorClientTests
{
    /// <summary>Deadline for any test that waits on the pipeline.</summary>
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>A transport outage on a metrics read is answered as an unavailable agent, not thrown.</summary>
    /// <remarks>
    /// The inner fake throws what the real transport throws for a refused connection
    /// (<see cref="AgentTransportFaults.ConnectRefused"/>), so the answer is the decorator's to give: the
    /// pipeline retries the refused connect twice and the shared mapping answers 503's error code.
    /// </remarks>
    [Fact]
    public async Task A_transport_outage_on_a_metrics_read_answers_agent_unavailable_instead_of_throwing()
    {
        var inner = new RecordingAgentMonitorClient { FailuresBeforeSuccess = int.MaxValue };

        var result = await Decorate(inner).GetHostMetricsAsync(default).WaitAsync(TestTimeout);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentUnavailable", result.Error!.Code);
        Assert.Equal(ErrorType.Unavailable, result.Error.Type);
        Assert.Equal(3, inner.Calls);
    }

    /// <summary>The metrics read retries a transport failure through the pipeline.</summary>
    [Fact]
    public async Task The_metrics_read_retries_a_transport_failure_through_the_pipeline()
    {
        var inner = new RecordingAgentMonitorClient { FailuresBeforeSuccess = 1 };

        var result = await Decorate(inner).GetHostMetricsAsync(default).WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
    }

    /// <summary>The statuses read retries a transport failure through the pipeline.</summary>
    [Fact]
    public async Task The_statuses_read_retries_a_transport_failure_through_the_pipeline()
    {
        var inner = new RecordingAgentMonitorClient { FailuresBeforeSuccess = 1 };

        var result = await Decorate(inner).GetServiceStatusesAsync(default).WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
    }

    /// <summary>The disk usage read retries a transport failure through the pipeline.</summary>
    [Fact]
    public async Task The_disk_usage_read_retries_a_transport_failure_through_the_pipeline()
    {
        var inner = new RecordingAgentMonitorClient { FailuresBeforeSuccess = 1 };

        var result = await Decorate(inner).GetAccountsDiskUsageAsync(default).WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
    }

    /// <summary>A metrics read that never returns is abandoned by the pipelines timeout.</summary>
    [Fact]
    public async Task A_metrics_read_that_never_returns_is_abandoned_by_the_pipelines_timeout()
    {
        var inner = new RecordingAgentMonitorClient { Hangs = true };

        var result = await Decorate(inner).GetHostMetricsAsync(default).WaitAsync(TestTimeout);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentUnavailable", result.Error!.Code);
        Assert.Equal(ErrorType.Unavailable, result.Error.Type);
    }

    /// <summary>Wraps the recording client in the decorator under the real pipeline.</summary>
    /// <param name="inner">The recording client to wrap.</param>
    /// <returns>The decorated client.</returns>
    private static ResilientAgentMonitorClient Decorate(RecordingAgentMonitorClient inner)
    {
        return new ResilientAgentMonitorClient(inner, OperationPipelineRegistry.WithOperationTimeout(1));
    }
}
