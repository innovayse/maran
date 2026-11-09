using Maran.Agent.Client.Services.DnsService;
using Maran.Host.Resilience;
using Maran.SharedKernel.Results;

namespace Maran.Host.Tests.Resilience;

/// <summary>What the DNS decorator does: every call goes through the pipeline, arguments unchanged.</summary>
/// <remarks>
/// Each test asserts both halves the decorator can get wrong: that the call was EXECUTED through the
/// pipeline, visible as a retried transport failure, and that every argument reached the inner client
/// unchanged. A method that called straight through would run with no timeout at all.
/// </remarks>
public sealed class ResilientAgentDnsClientTests
{
    /// <summary>Deadline for any test that waits on the pipeline.</summary>
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The zone a put call carries through the decorator.</summary>
    private static readonly DnsZoneDefinition Zone = new(
        "example.com",
        2026110701,
        "ns1.example.net",
        "hostmaster.example.net",
        ["ns1.example.net"],
        []);

    /// <summary>Every call without arguments retries a refused connection once and then succeeds.</summary>
    [Fact]
    public async Task The_argument_free_calls_retry_a_transport_failure_through_the_pipeline()
    {
        var results = new List<(bool Ok, int Calls)>();
        foreach (var call in new Func<ResilientAgentDnsClient, Task<bool>>[]
        {
            async client => { return (await client.EnableAsync(default)).IsSuccess; },
            async client => { return (await client.DisableAsync(default)).IsSuccess; },
            async client => { return (await client.GetStatusAsync(default)).IsSuccess; },
            async client => { return (await client.ListZonesAsync(default)).IsSuccess; },
        })
        {
            var inner = new RecordingAgentDnsClient { FailuresBeforeSuccess = 1 };

            var ok = await call(Decorate(inner)).WaitAsync(TestTimeout);

            results.Add((ok, inner.Calls));
        }

        Assert.All(results, row => { Assert.Equal((true, 2), row); });
    }

    /// <summary>The zone calls retry through the pipeline with their arguments intact.</summary>
    [Fact]
    public async Task The_zone_calls_retry_through_the_pipeline_with_their_arguments_intact()
    {
        var put = new RecordingAgentDnsClient { FailuresBeforeSuccess = 1 };
        var delete = new RecordingAgentDnsClient { FailuresBeforeSuccess = 1 };
        var get = new RecordingAgentDnsClient { FailuresBeforeSuccess = 1 };

        var served = await Decorate(put).PutZoneAsync(Zone, default).WaitAsync(TestTimeout);
        var removed = await Decorate(delete).DeleteZoneAsync("example.org", default).WaitAsync(TestTimeout);
        var read = await Decorate(get).GetZoneAsync("example.net", default).WaitAsync(TestTimeout);

        Assert.True(served.IsSuccess);
        Assert.Equal(2, put.Calls);
        Assert.Same(Zone, put.LastZone);
        Assert.True(removed.IsSuccess);
        Assert.Equal(2, delete.Calls);
        Assert.Equal("example.org", delete.LastZoneName);
        Assert.True(read.IsSuccess);
        Assert.Equal(2, get.Calls);
        Assert.Equal("example.net", get.LastZoneName);
    }

    /// <summary>A transport outage is answered as an unavailable agent, not thrown.</summary>
    [Fact]
    public async Task A_transport_outage_answers_agent_unavailable_instead_of_throwing()
    {
        var inner = new RecordingAgentDnsClient { FailuresBeforeSuccess = int.MaxValue };

        var result = await Decorate(inner).PutZoneAsync(Zone, default).WaitAsync(TestTimeout);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentUnavailable", result.Error!.Code);
        Assert.Equal(ErrorType.Unavailable, result.Error.Type);
    }

    /// <summary>A call that never returns is abandoned by the pipeline's timeout, entered once.</summary>
    /// <remarks>
    /// The timeout does not stop the agent's work, so a retry would apply a second put beside the first.
    /// </remarks>
    [Fact]
    public async Task A_put_that_never_returns_is_abandoned_by_the_pipelines_timeout_and_not_started_twice()
    {
        var inner = new RecordingAgentDnsClient { Hangs = true };

        var result = await Decorate(inner).PutZoneAsync(Zone, default).WaitAsync(TestTimeout);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentUnavailable", result.Error!.Code);
        Assert.Equal(1, inner.Calls);
    }

    /// <summary>Wraps the recording client in the decorator under the real pipeline.</summary>
    /// <param name="inner">The recording client to wrap.</param>
    /// <returns>The decorated client.</returns>
    private static ResilientAgentDnsClient Decorate(RecordingAgentDnsClient inner)
    {
        return new ResilientAgentDnsClient(inner, OperationPipelineRegistry.WithOperationTimeout(1));
    }
}
