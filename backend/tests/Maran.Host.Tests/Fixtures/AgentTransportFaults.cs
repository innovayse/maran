using Grpc.Core;
using Maran.Agent.Client.Channels;
using Maran.Agent.Client.Tests.TestSupport;
using Maran.Agent.V1;

namespace Maran.Host.Tests.Fixtures;

/// <summary>
/// The exceptions the agent transport really throws, for the fakes that stand in for a dead agent.
/// </summary>
/// <remarks>
/// A fake that throws a bare <see cref="System.Net.Sockets.SocketException"/> proves nothing: the
/// real transport never lets one out, and a retry predicate written against that fake retried
/// nothing in production (issue #87). The refused-connect fault here is produced by one real call
/// through <see cref="AgentChannel"/> to a path that does not exist.
/// </remarks>
internal static class AgentTransportFaults
{
    /// <summary>The exception of one real call refused for want of a socket, made once and reused.</summary>
    private static readonly Lazy<RpcException> Refused = new(MakeRefused);

    /// <summary>What a call throws when the connection to the agent could not be established.</summary>
    /// <returns>The <see cref="RpcException"/> the real transport raised for a missing socket.</returns>
    public static RpcException ConnectRefused()
    {
        return Refused.Value;
    }

    /// <summary>What a call throws when the connection was lost after the request was sent.</summary>
    /// <returns>An <see cref="RpcException"/> with <see cref="StatusCode.Unavailable"/> that is not a failed connect.</returns>
    /// <remarks>SYNTHETIC: built by hand in the shape a reset response stream has, not measured.</remarks>
    public static RpcException AfterRequestSent()
    {
        return new RpcException(new Status(
            StatusCode.Unavailable,
            "Error reading next message.",
            new HttpRequestException(HttpRequestError.ResponseEnded, "The response ended prematurely.")));
    }

    /// <summary>Makes one real call to a socket path that does not exist and returns what it threw.</summary>
    /// <returns>The thrown exception.</returns>
    private static RpcException MakeRefused()
    {
        using var missing = UnixSocketAgentDouble.Missing();
        using var channel = AgentChannel.CreateUnixSocket(missing.SocketPath);
        var client = new SystemService.SystemServiceClient(channel);

        try
        {
            client.GetAgentInfo(new GetAgentInfoRequest());
        }
        catch (RpcException exception)
        {
            return exception;
        }

        throw new InvalidOperationException("A call to a missing socket did not fail.");
    }
}
