using System.Net.Sockets;
using Grpc.Net.Client;

namespace Maran.Agent.Client.Channels;

/// <summary>Builds gRPC channels over the agent's unix socket.</summary>
public static class AgentChannel
{
    /// <summary>Creates a channel connected to <paramref name="socketPath"/>.</summary>
    /// <param name="socketPath">Filesystem path of the agent's unix domain socket.</param>
    /// <returns>A channel whose HTTP/2 transport connects over the given unix socket.</returns>
    public static GrpcChannel CreateUnixSocket(string socketPath)
    {
        var endpoint = new UnixDomainSocketEndPoint(socketPath);
        return GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpHandler = new SocketsHttpHandler
            {
                ConnectCallback = async (_, ct) =>
                {
                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    await socket.ConnectAsync(endpoint, ct);
                    return new NetworkStream(socket, ownsSocket: true);
                },
            },
        });
    }
}
