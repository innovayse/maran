using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using Grpc.Net.Client;
using Maran.Agent.Client.Models;
using Maran.SharedKernel.Interfaces;

namespace Maran.Agent.Client.Channels;

/// <summary>Builds gRPC channels to an agent: over its unix socket, or to a managed node over mutual TLS.</summary>
public static class AgentChannel
{
    /// <summary>
    /// How long a pooled connection to a node lives before it is replaced. Finite so that a
    /// rotated pin or identity takes effect on its own instead of after the next restart.
    /// </summary>
    private static readonly TimeSpan NodeConnectionLifetime = TimeSpan.FromMinutes(10);

    /// <summary>How long the panel waits for a node to accept a TCP connection.</summary>
    private static readonly TimeSpan NodeConnectTimeout = TimeSpan.FromSeconds(10);

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
    /// <para>
    /// The channel owns its handler (<c>DisposeHttpClient</c>), as <see cref="CreateMutualTls"/> does:
    /// disposing the channel closes the pooled connection to the socket instead of leaving it to the
    /// finalizer.
    /// </para>
    /// </remarks>
    public static GrpcChannel CreateUnixSocket(string socketPath)
    {
        var endpoint = new UnixDomainSocketEndPoint(socketPath);
        return GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            ThrowOperationCanceledOnCancellation = true,
            // The pool disposes a channel it replaces or retires; the handler and its connections go with it.
            DisposeHttpClient = true,
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

    /// <summary>Creates a channel to a managed node's fleet listener over mutual TLS 1.3.</summary>
    /// <param name="endpoint">Where the node is, the panel identity to present, and the node's pin.</param>
    /// <param name="clock">The panel's clock, for the validity check of the node's certificate.</param>
    /// <returns>A channel that presents the panel's certificate and accepts only the pinned node certificate.</returns>
    /// <remarks>
    /// <para>
    /// <b>TLS 1.3 only</b>, set on the handler, so a node that offers less is refused rather than
    /// downgraded to. The server certificate is judged by <see cref="PinnedCertificateCheck"/> alone;
    /// the library's chain and name verdicts are ignored because they cannot mean anything for a
    /// self-signed certificate.
    /// </para>
    /// <para>
    /// The client identity goes in as a <see cref="SslStreamCertificateContext"/> built <c>offline</c>:
    /// the context bypasses issuer-hint filtering, and offline stops the TLS stack from fetching
    /// revocation or issuer information, which would be an outbound call the panel must not make
    /// (rules/security.md item 10).
    /// </para>
    /// <para>
    /// <b>A node that refuses the panel's certificate is seen late.</b> Under TLS 1.3 the client
    /// considers the handshake finished before the server has verified the client certificate, so the
    /// refusal surfaces as a failed first call, not as an authentication error here. A refusal
    /// by the panel itself (pin, expiry, TLS version) arrives as an <c>Internal</c> status over a
    /// <c>SecureConnectionError</c>, which <see cref="Errors.AgentTransportFailure"/> classifies as an
    /// unreachable node. Cancellation behaves as for <see cref="CreateUnixSocket"/>.
    /// </para>
    /// </remarks>
    public static GrpcChannel CreateMutualTls(MutualTlsAgentEndpoint endpoint, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(clock);

        var address = new UriBuilder(Uri.UriSchemeHttps, endpoint.Host, endpoint.Port).Uri;
        return GrpcChannel.ForAddress(address, new GrpcChannelOptions
        {
            ThrowOperationCanceledOnCancellation = true,
            // The pool replaces a node's channel when its endpoint changes; the handler must go with it.
            DisposeHttpClient = true,
            HttpHandler = new SocketsHttpHandler
            {
                ConnectTimeout = NodeConnectTimeout,
                PooledConnectionLifetime = NodeConnectionLifetime,
                SslOptions = new SslClientAuthenticationOptions
                {
                    EnabledSslProtocols = SslProtocols.Tls13,
                    ClientCertificateContext = SslStreamCertificateContext.Create(
                        endpoint.ClientCertificate,
                        additionalCertificates: null,
                        offline: true),
                    RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                    {
                        return PinnedCertificateCheck.Accepts(
                            certificate,
                            endpoint.PinnedServerFingerprint,
                            clock.UtcNow);
                    },
                },
            },
        });
    }
}
