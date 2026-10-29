using System.Net.Sockets;
using Grpc.Net.Client;

namespace Maran.Agent.Client.Channels;

/// <summary>Builds gRPC channels over the agent's unix socket.</summary>
public static class AgentChannel
{
    /// <summary>Creates a channel connected to <paramref name="socketPath"/>.</summary>
    /// <param name="socketPath">Filesystem path of the agent's unix domain socket.</param>
    /// <returns>A channel whose HTTP/2 transport connects over the given unix socket.</returns>
    /// <remarks>
    /// <para>
    /// <b>A cancelled call throws <see cref="OperationCanceledException"/>, not a gRPC status.</b>
    /// <c>ThrowOperationCanceledOnCancellation</c> is set because the panel has two different things
    /// to say about a call that stopped, and the library's default makes them one: left unset, both
    /// the caller giving up and the resilience pipeline's timeout arrive as an <c>RpcException</c>
    /// with <c>StatusCode.Cancelled</c>. With it set, the caller's cancellation is the cancellation
    /// exception every <c>catch (OperationCanceledException)</c> around agent work was written for —
    /// none of which could see a gRPC cancellation before — and the pipeline's timeout is the
    /// <c>TimeoutRejectedException</c> its timeout strategy produces only from that exception, which
    /// is what lets the operation pipeline refuse to retry a timeout while still answering it as an
    /// unavailable agent.
    /// </para>
    /// <para>
    /// The socket is disposed when the connect fails. The callback owns it until it hands it to the
    /// stream, and a connect refused by a missing or abandoned socket file is exactly the failure the
    /// operation pipeline now retries, so an outage would otherwise leave three sockets per call for
    /// the finalizer.
    /// </para>
    /// </remarks>
    public static GrpcChannel CreateUnixSocket(string socketPath)
    {
        var endpoint = new UnixDomainSocketEndPoint(socketPath);
        return GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            ThrowOperationCanceledOnCancellation = true,
            HttpHandler = new SocketsHttpHandler
            {
                ConnectCallback = async (_, ct) =>
                {
                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    try
                    {
                        await socket.ConnectAsync(endpoint, ct);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (Exception)
                    {
                        socket.Dispose();
                        throw;
                    }
                },
            },
        });
    }
}
