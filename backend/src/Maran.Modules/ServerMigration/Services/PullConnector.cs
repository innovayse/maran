using System.Net;
using System.Net.Sockets;
using Maran.Modules.ServerMigration.Domain.Policies;
using Maran.Modules.ServerMigration.Interfaces;

namespace Maran.Modules.ServerMigration.Services;

/// <summary>
/// Makes the one connection of a pull and refuses a destination that is not allowed, on the address actually
/// connected to (issue #127). The primary handler under the module's named client, and the only place that client
/// opens a socket.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three locks, none of them a check of the URL's text.</b> (1) The name is resolved HERE and every address it
/// resolves to is judged by <see cref="PullAddressPolicy"/>; refused ones are dropped, and when none is left the
/// connection is refused. (2) The socket is connected to a literal address from that list, so what was judged is
/// what is connected to (the name is never handed to the OS to resolve again, which would let a second answer
/// differ from the first). (3) The socket's remote address is judged once more after the connection is made.
/// </para>
/// <para>
/// <b>Not here, and deliberately not switchable:</b> certificate validation. The handler's TLS options are left at
/// their defaults, which validate the chain and the name; there is no setting, argument or environment value that
/// relaxes them. Redirects are not followed and cookies are not kept.
/// </para>
/// </remarks>
public sealed class PullConnector
{
    /// <summary>The addresses this server's own interfaces hold.</summary>
    private readonly ILocalAddressSource _local;

    /// <summary>Creates the connector.</summary>
    /// <param name="local">Tells which addresses are this server's own.</param>
    public PullConnector(ILocalAddressSource local)
    {
        _local = local;
    }

    /// <summary>Builds the handler the named client sends through: no redirects, no cookies, a bounded connect, and this connector.</summary>
    /// <returns>The handler the factory uses as the innermost one.</returns>
    public HttpMessageHandler CreateHandler()
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            ConnectTimeout = MigrationPullLimits.ConnectTimeout,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectCallback = ConnectAsync,
        };
    }

    /// <summary>Tells whether an exception is this connector's refusal of an address, however deeply the HTTP stack wrapped it.</summary>
    /// <param name="exception">The exception the request raised.</param>
    /// <returns><c>true</c> when it came from <see cref="ConnectAsync"/> refusing the destination.</returns>
    public static bool IsAddressRefusal(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is AddressRefusedException)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Resolves the host, drops the refused addresses and connects to the first of the rest that answers.</summary>
    /// <param name="context">The host and port the request is for.</param>
    /// <param name="cancellationToken">Cancels the connection.</param>
    /// <returns>The connected stream; TLS is applied by the handler after this returns.</returns>
    /// <exception cref="AddressRefusedException">Every address the name resolves to is one the policy refuses.</exception>
    private async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var own = _local.Read();
        var resolved = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        var allowed = resolved.Where(address => { return !PullAddressPolicy.IsRefused(address, own); }).ToList();

        if (allowed.Count == 0)
        {
            throw new AddressRefusedException();
        }

        Exception? last = null;

        foreach (var address in allowed)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);

                if (socket.RemoteEndPoint is not IPEndPoint remote || PullAddressPolicy.IsRefused(remote.Address, own))
                {
                    throw new AddressRefusedException();
                }

                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException exception)
            {
                socket.Dispose();
                last = exception;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw new IOException("No address of the host accepted the connection.", last);
    }

    /// <summary>The destination was refused by <see cref="PullAddressPolicy"/>. Raised inside the connect callback, recognised by <see cref="IsAddressRefusal"/>, and never leaves this class's callers as a type.</summary>
    private sealed class AddressRefusedException : IOException
    {
        /// <summary>Creates the exception with a fixed text that names no address.</summary>
        public AddressRefusedException()
            : base("The destination address is not allowed.")
        {
        }
    }
}
