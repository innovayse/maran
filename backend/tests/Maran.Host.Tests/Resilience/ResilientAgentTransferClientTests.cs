using Maran.Agent.Client.Services.TransferService;
using Maran.Host.Resilience;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;

namespace Maran.Host.Tests.Resilience;

/// <summary>What the transfer decorator does: the unary calls go through the pipeline, the stream does not.</summary>
public sealed class ResilientAgentTransferClientTests
{
    /// <summary>Deadline for any test that waits on the pipeline.</summary>
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>A key generation retries a connect that failed and then succeeds.</summary>
    [Fact]
    public async Task Generating_a_key_retries_a_transport_failure_through_the_pipeline()
    {
        var inner = new RecordingAgentTransferClient { FailuresBeforeSuccess = 1 };

        var result = await NewClient(inner).GenerateSourceKeyAsync(default).WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
    }

    /// <summary>A scan retries a transport failure through the pipeline.</summary>
    [Fact]
    public async Task Scanning_retries_a_transport_failure_through_the_pipeline()
    {
        var inner = new RecordingAgentTransferClient { FailuresBeforeSuccess = 1 };

        var result = await NewClient(inner).ScanHostKeyAsync(Endpoint(), default).WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
        Assert.Equal("203.0.113.9", inner.LastHost);
    }

    /// <summary>A test retries a transport failure through the pipeline.</summary>
    [Fact]
    public async Task Testing_retries_a_transport_failure_through_the_pipeline()
    {
        var inner = new RecordingAgentTransferClient { FailuresBeforeSuccess = 1 };

        var result = await NewClient(inner).TestSourceAsync(Endpoint(), Credential(), default).WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
    }

    /// <summary>A transport outage is answered as an unavailable agent with no reason, not thrown.</summary>
    [Fact]
    public async Task A_transport_outage_answers_agent_unavailable_with_no_reason_instead_of_throwing()
    {
        var inner = new RecordingAgentTransferClient { FailuresBeforeSuccess = int.MaxValue };

        var result = await NewClient(inner).TestSourceAsync(Endpoint(), Credential(), default).WaitAsync(TestTimeout);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentUnavailable", result.Error!.Code);
        Assert.Equal(ErrorType.Unavailable, result.Error.Type);
        Assert.Equal(SourceFailureReason.Unspecified, result.Reason);
        Assert.Equal(3, inner.Calls);
    }

    /// <summary>A source that answered and refused is an answer: its reason survives and it is not repeated.</summary>
    /// <remarks>
    /// The inverse of the outage test: a call that reached the agent and was refused by the source is not a
    /// failed connect, so it is entered once, and the decorator returns what it was given without altering
    /// the code or dropping the reason.
    /// </remarks>
    [Fact]
    public async Task A_refusal_by_the_source_passes_through_with_its_reason_and_is_not_retried()
    {
        var inner = new RecordingAgentTransferClient { RefuseWith = SourceFailureReason.HostKeyChanged };

        var result = await NewClient(inner).TestSourceAsync(Endpoint(), Credential(), default).WaitAsync(TestTimeout);

        Assert.False(result.IsSuccess);
        Assert.Equal(SourceFailureReason.HostKeyChanged, result.Reason);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        Assert.Equal(1, inner.Calls);
    }

    /// <summary>A failed inventory stream is never retried.</summary>
    /// <remarks>
    /// A replayed stream that had already delivered accounts would hand the caller the same accounts twice,
    /// so the stream is passed straight through: one entry into the inner client is the whole assertion.
    /// </remarks>
    [Fact]
    public async Task A_failed_inventory_stream_is_never_retried()
    {
        var inner = new RecordingAgentTransferClient { StreamFails = true };

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await DrainAsync(inner).WaitAsync(TestTimeout);
        });

        Assert.Equal(1, inner.StreamStarts);
    }

    /// <summary>The inventory stream forwards the endpoint it was asked for.</summary>
    /// <remarks>
    /// The inverse control for the non-retry test: a decorator that had stopped calling the inner client at
    /// all would satisfy "entered once" trivially by entering it never.
    /// </remarks>
    [Fact]
    public async Task The_inventory_stream_forwards_the_endpoint_it_was_asked_for()
    {
        var inner = new RecordingAgentTransferClient();

        await DrainAsync(inner).WaitAsync(TestTimeout);

        Assert.Equal("203.0.113.9", inner.LastHost);
        Assert.Equal(1, inner.StreamStarts);
    }

    /// <summary>Reads the decorator's inventory stream to its end.</summary>
    /// <param name="inner">The recording client to wrap.</param>
    /// <returns>A task that completes when the stream has ended.</returns>
    private static async Task DrainAsync(RecordingAgentTransferClient inner)
    {
        await using var drained = NewClient(inner)
            .ReadSourceInventoryAsync(Endpoint(), Credential(), default)
            .GetAsyncEnumerator();
        while (await drained.MoveNextAsync())
        {
            // The events themselves are the client's contract, not the decorator's.
        }
    }

    /// <summary>The endpoint every test aims at.</summary>
    /// <returns>An endpoint on a documentation address.</returns>
    private static AgentSourceEndpoint Endpoint()
    {
        return new AgentSourceEndpoint("203.0.113.9", 22, "root");
    }

    /// <summary>A credential for the calls that take one.</summary>
    /// <returns>A credential whose key is a placeholder.</returns>
    private static AgentSourceCredential Credential()
    {
        return new AgentSourceCredential(new SensitiveString("KEY"), "ssh-ed25519 AAAAx");
    }

    /// <summary>Builds the decorator over a pipeline whose timeout is long enough not to interfere.</summary>
    /// <param name="inner">The recording client to wrap.</param>
    /// <returns>The decorator under test.</returns>
    private static ResilientAgentTransferClient NewClient(RecordingAgentTransferClient inner)
    {
        return new ResilientAgentTransferClient(inner, OperationPipelineRegistry.WithOperationTimeout(30));
    }
}
