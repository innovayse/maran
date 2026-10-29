using Grpc.Core;
using Maran.Agent.Client.Channels;
using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Tests.Errors;

/// <summary>
/// The one classification of a failed transport: which failures are a connect that never happened
/// (safe to repeat), which mean the agent could not be reached, and what the panel answers with.
/// </summary>
/// <remarks>
/// The connect-failure tests run over the REAL transport — a real channel to a real socket path —
/// because the defect this type fixes was a predicate written against an exception the transport
/// never throws, checked only by fakes that threw it.
/// </remarks>
public sealed class AgentTransportFailureTests
{
    /// <summary>Deadline for any call to a double, so a stalled transport fails instead of hanging.</summary>
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>A socket path that does not exist is a connect failure.</summary>
    [Fact]
    public async Task A_missing_agent_socket_is_classified_as_a_connect_failure()
    {
        using var agent = UnixSocketAgentDouble.Missing();

        var thrown = await ThrownByRealCallAsync(agent);

        Assert.True(AgentTransportFailure.IsConnectFailure(thrown));
    }

    /// <summary>A socket file nobody listens on is a connect failure.</summary>
    [Fact]
    public async Task A_stale_agent_socket_is_classified_as_a_connect_failure()
    {
        using var agent = UnixSocketAgentDouble.Stale();

        var thrown = await ThrownByRealCallAsync(agent);

        Assert.True(AgentTransportFailure.IsConnectFailure(thrown));
    }

    /// <summary>
    /// An unavailable status raised after the request was sent is not a connect failure, so it is
    /// never repeated: the agent may be doing the work.
    /// </summary>
    [Fact]
    public void An_unavailable_status_raised_after_the_request_was_sent_is_not_a_connect_failure()
    {
        var thrown = new RpcException(new Status(
            StatusCode.Unavailable,
            "Error reading next message.",
            new HttpRequestException(HttpRequestError.ResponseEnded, "The response ended prematurely.")));

        Assert.False(AgentTransportFailure.IsConnectFailure(thrown));
        Assert.True(AgentTransportFailure.IsUnreachable(thrown));
    }

    /// <summary>A connection error under any status but unavailable is not a connect failure.</summary>
    [Fact]
    public void A_deadline_carrying_a_connect_error_is_not_a_connect_failure()
    {
        var thrown = new RpcException(new Status(
            StatusCode.DeadlineExceeded,
            "Deadline Exceeded",
            new HttpRequestException(HttpRequestError.ConnectionError, "refused")));

        Assert.False(AgentTransportFailure.IsConnectFailure(thrown));
    }

    /// <summary>An exception that is not a gRPC status is not a connect failure.</summary>
    [Fact]
    public void An_exception_that_is_not_a_grpc_status_is_not_a_connect_failure()
    {
        Assert.False(AgentTransportFailure.IsConnectFailure(
            new HttpRequestException(HttpRequestError.ConnectionError, "refused")));
    }

    /// <summary>Unavailable, deadline and an unrequested cancellation are one answer: a 503 kind.</summary>
    /// <param name="status">The status the transport raised.</param>
    [Theory]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.DeadlineExceeded)]
    [InlineData(StatusCode.Cancelled)]
    public void Unavailable_deadline_and_an_unrequested_cancellation_all_answer_agent_unavailable(StatusCode status)
    {
        var error = AgentTransportFailure.ToError(new RpcException(new Status(status, "detail")));

        Assert.Equal("AgentUnavailable", error.Code);
        Assert.Equal(ErrorType.Unavailable, error.Type);
    }

    /// <summary>An rpc the agent does not serve is answered as not implemented.</summary>
    [Fact]
    public void An_unimplemented_rpc_answers_agent_not_implemented()
    {
        var error = AgentTransportFailure.ToError(new RpcException(new Status(StatusCode.Unimplemented, "detail")));

        Assert.Equal("AgentNotImplemented", error.Code);
        Assert.Equal(ErrorType.Failure, error.Type);
    }

    /// <summary>A refusal by the peer guard or a framework fault is an invalid response, a failure kind.</summary>
    /// <param name="status">The status the transport raised.</param>
    [Theory]
    [InlineData(StatusCode.PermissionDenied)]
    [InlineData(StatusCode.Internal)]
    public void Permission_denied_and_internal_answer_agent_invalid_response(StatusCode status)
    {
        var error = AgentTransportFailure.ToError(new RpcException(new Status(status, "detail")));

        Assert.Equal("AgentInvalidResponse", error.Code);
        Assert.Equal(ErrorType.Failure, error.Type);
    }

    /// <summary>The transport's own text never reaches the answer.</summary>
    [Fact]
    public void The_status_detail_is_not_carried_into_the_answer()
    {
        var error = AgentTransportFailure.ToError(
            new RpcException(new Status(StatusCode.Unavailable, "/run/maran/agent.sock refused")));

        Assert.DoesNotContain("agent.sock", error.Code, StringComparison.Ordinal);
    }

    /// <summary>Unreachable covers unavailable and deadline, and not the other statuses.</summary>
    /// <param name="status">The status the transport raised.</param>
    /// <param name="expected">Whether the agent counts as unreachable.</param>
    [Theory]
    [InlineData(StatusCode.Unavailable, true)]
    [InlineData(StatusCode.DeadlineExceeded, true)]
    [InlineData(StatusCode.Cancelled, false)]
    [InlineData(StatusCode.Internal, false)]
    [InlineData(StatusCode.Unimplemented, false)]
    public void Unreachable_covers_unavailable_and_deadline_but_not_cancelled_or_internal(
        StatusCode status,
        bool expected)
    {
        Assert.Equal(expected, AgentTransportFailure.IsUnreachable(new RpcException(new Status(status, "detail"))));
    }

    /// <summary>Makes one real unary call to <paramref name="agent"/> and returns what the transport threw.</summary>
    /// <param name="agent">The double standing in for the agent.</param>
    /// <returns>The exception thrown by the call.</returns>
    private static async Task<RpcException> ThrownByRealCallAsync(UnixSocketAgentDouble agent)
    {
        using var channel = AgentChannel.CreateUnixSocket(agent.SocketPath);
        var client = new Maran.Agent.V1.SystemService.SystemServiceClient(channel);

        return await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await client.GetAgentInfoAsync(new Maran.Agent.V1.GetAgentInfoRequest()).ResponseAsync.WaitAsync(TestTimeout);
        });
    }
}
