using Grpc.Core;
using Maran.Agent.Client.Channels;
using Maran.Agent.Client.Services.SystemService;
using Maran.Agent.Client.Tests.TestSupport;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Agent.Client.Tests.Services.SystemService;

/// <summary>
/// Mapping contract of AgentSystemClient: proto oneof to Result, the contract-revision window, and a
/// transport failure answered as a result rather than thrown.
/// </summary>
public sealed class AgentSystemClientTests
{
    /// <summary>Builds a successful handshake reporting the given contract revision.</summary>
    /// <param name="protoVersion">The revision the stub agent reports.</param>
    /// <returns>The response.</returns>
    private static GetAgentInfoResponse HandshakeReporting(uint protoVersion)
    {
        return new GetAgentInfoResponse
        {
            Ok = new AgentInfo
            {
                Version = "0.1.0",
                DistroId = "ubuntu",
                Family = DistroFamily.Debian,
                ProtoVersion = protoVersion,
                BackupRoot = "/var/backups/maran",
            },
        };
    }

    /// <summary>Ok payload maps to success result.</summary>
    [Fact]
    public async Task Ok_payload_maps_to_success_result()
    {
        var response = HandshakeReporting(AgentProtoVersionWindow.Maximum);
        var client = new AgentSystemClient(new StubSystemService(response), NullLogger<AgentSystemClient>.Instance);

        var result = await client.GetInfoAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("ubuntu", result.Value.DistroId);
    }

    /// <summary>Error payload maps to failed result with agent code.</summary>
    [Fact]
    public async Task Error_payload_maps_to_failed_result_with_agent_code()
    {
        var response = new GetAgentInfoResponse
        {
            Error = new AgentError { Code = ErrorCode.SystemFailure, Message = "boom" },
        };
        var client = new AgentSystemClient(new StubSystemService(response), NullLogger<AgentSystemClient>.Instance);

        var result = await client.GetInfoAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
    }

    /// <summary>Unset oneof maps to invalid response error.</summary>
    [Fact]
    public async Task Unset_oneof_maps_to_invalid_response_error()
    {
        var response = new GetAgentInfoResponse();
        var client = new AgentSystemClient(new StubSystemService(response), NullLogger<AgentSystemClient>.Instance);

        var result = await client.GetInfoAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>A handshake inside the window is admitted with the agent's own identity.</summary>
    [Fact]
    public async Task A_handshake_inside_the_window_is_admitted_with_the_agents_identity()
    {
        var client = new AgentSystemClient(
            new StubSystemService(HandshakeReporting(AgentProtoVersionWindow.Minimum)),
            NullLogger<AgentSystemClient>.Instance);

        var result = await client.GetInfoAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AgentProtoVersionWindow.Minimum, result.Value.ProtoVersion);
        Assert.Equal("0.1.0", result.Value.Version);
        Assert.Equal("/var/backups/maran", result.Value.BackupRoot);
    }

    /// <summary>
    /// A handshake outside the window is answered as incompatible, classed as unavailable, and logged
    /// at Warning with the revision the agent reported.
    /// </summary>
    /// <param name="revision">The revision the stub agent reports.</param>
    [Theory]
    [InlineData(AgentProtoVersionWindow.Minimum - 1)]
    [InlineData(0u)]
    [InlineData(AgentProtoVersionWindow.Maximum + 1)]
    public async Task A_handshake_outside_the_window_is_answered_incompatible(uint revision)
    {
        var logger = new RecordingLogger<AgentSystemClient>();
        var client = new AgentSystemClient(new StubSystemService(HandshakeReporting(revision)), logger);

        var result = await client.GetInfoAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AgentProtoVersionWindow.IncompatibleCode, result.Error!.Code);
        Assert.Equal(ErrorType.Unavailable, result.Error.Type);

        var entry = Assert.Single(logger.Messages);
        Assert.Equal(LogLevel.Warning, Assert.Single(logger.Levels));
        Assert.Contains(revision.ToString(System.Globalization.CultureInfo.InvariantCulture), entry, StringComparison.Ordinal);
    }

    /// <summary>A refused handshake never hands on the agent's identity or its backup root.</summary>
    [Fact]
    public async Task A_failed_handshake_never_hands_on_the_backup_root()
    {
        var client = new AgentSystemClient(
            new StubSystemService(HandshakeReporting(AgentProtoVersionWindow.Maximum + 1)),
            NullLogger<AgentSystemClient>.Instance);

        var result = await client.GetInfoAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);

        // Reading the value of a failed result throws, so no caller can reach the identity or the
        // backup root of an agent the window refused.
        Assert.Throws<InvalidOperationException>(() =>
        {
            return result.Value;
        });
    }

    /// <summary>
    /// The handshake against a socket nobody listens on is a failed result, not an exception, over the
    /// real transport.
    /// </summary>
    [Fact]
    public async Task A_missing_agent_socket_is_a_result_not_an_exception()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"maran-absent-{Guid.NewGuid():N}.sock");
        using var channel = AgentChannel.CreateUnixSocket(missing);
        var client = new AgentSystemClient(channel, NullLogger<AgentSystemClient>.Instance);

        var result = await client.GetInfoAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentUnavailable", result.Error!.Code);
        Assert.Equal(ErrorType.Unavailable, result.Error.Type);
    }

    /// <summary>A transport status the agent reports is answered as unavailable, not thrown.</summary>
    /// <param name="status">The status the stub transport fails with.</param>
    [Theory]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.DeadlineExceeded)]
    public async Task A_transport_failure_of_the_handshake_is_a_result_not_an_exception(StatusCode status)
    {
        var failure = new RpcException(new Status(status, "transport"));
        var client = new AgentSystemClient(new ThrowingSystemService(failure), NullLogger<AgentSystemClient>.Instance);

        var result = await client.GetInfoAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentUnavailable", result.Error!.Code);
        Assert.Equal(ErrorType.Unavailable, result.Error.Type);
    }

    /// <summary>A caller's cancellation surfaces as a cancellation, never as an unavailable agent.</summary>
    [Fact]
    public async Task A_cancelled_handshake_is_rethrown_as_a_cancellation_never_mapped()
    {
        using var source = new CancellationTokenSource();
        var failure = new RpcException(new Status(StatusCode.Cancelled, "cancelled"));
        var client = new AgentSystemClient(
            new ThrowingSystemService(failure, source),
            NullLogger<AgentSystemClient>.Instance);

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () =>
            {
                await client.GetInfoAsync(source.Token);
            });

        Assert.Equal(source.Token, thrown.CancellationToken);
    }

    /// <summary>A Cancelled status while the caller's token is live means the other side gave up: unavailable.</summary>
    [Fact]
    public async Task A_cancelled_status_without_a_cancelled_token_is_answered_unavailable()
    {
        var failure = new RpcException(new Status(StatusCode.Cancelled, "the other side cancelled"));
        var client = new AgentSystemClient(new ThrowingSystemService(failure), NullLogger<AgentSystemClient>.Instance);

        var result = await client.GetInfoAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentUnavailable", result.Error!.Code);
    }
}
