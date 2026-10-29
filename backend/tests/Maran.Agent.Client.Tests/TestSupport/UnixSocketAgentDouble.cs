using System.Net.Sockets;

namespace Maran.Agent.Client.Tests.TestSupport;

/// <summary>
/// A real unix-domain socket standing in for the agent, so a test drives the REAL transport
/// (<c>AgentChannel</c> and <c>Grpc.Net.Client</c>) instead of a fake that throws what the author
/// believes the transport throws. Built from the BCL only.
/// </summary>
/// <remarks>
/// Three shapes: <see cref="Missing"/> (the path does not exist), <see cref="Stale"/> (a socket file
/// is bound and nobody listens, which the kernel refuses) and <see cref="AcceptAndHold"/> (a
/// connection is accepted and read, and never answered). <c>Maran.Host.Tests</c> compiles this same
/// file as a linked item, so there is one copy; the type is internal and each project that links it
/// gets its own.
/// </remarks>
internal sealed class UnixSocketAgentDouble : IDisposable
{
    /// <summary>Stops the accept and read loops when the double is disposed.</summary>
    private readonly CancellationTokenSource _stop = new();

    /// <summary>The temporary directory that holds the socket file.</summary>
    private readonly string _directory;

    /// <summary>The bound socket, or <c>null</c> for a path that does not exist.</summary>
    private readonly Socket? _bound;

    /// <summary>The connections accepted so far, disposed with the double.</summary>
    private readonly List<Socket> _accepted = [];

    /// <summary>Completes when the first bytes of a client's request arrive.</summary>
    private readonly TaskCompletionSource _requestReceived =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Filesystem path of the socket the agent would listen on.</summary>
    public string SocketPath { get; }

    /// <summary>Completes once a client has sent its first bytes, so a test cancels a call that is in flight.</summary>
    public Task RequestReceived
    {
        get
        {
            return _requestReceived.Task;
        }
    }

    /// <summary>Creates the double in the given shape.</summary>
    /// <param name="bind">Whether a socket file is created at <see cref="SocketPath"/>.</param>
    /// <param name="listen">Whether the bound socket accepts connections.</param>
    private UnixSocketAgentDouble(bool bind, bool listen)
    {
        _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mrn" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_directory);
        SocketPath = System.IO.Path.Combine(_directory, "a.sock");

        if (!bind)
        {
            return;
        }

        _bound = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _bound.Bind(new UnixDomainSocketEndPoint(SocketPath));

        if (listen)
        {
            _bound.Listen(8);
            _ = AcceptLoopAsync(_bound);
        }
    }

    /// <summary>A path where no socket file exists.</summary>
    /// <returns>The double.</returns>
    public static UnixSocketAgentDouble Missing()
    {
        return new UnixSocketAgentDouble(bind: false, listen: false);
    }

    /// <summary>A socket file that is bound but never listened on, as an agent leaves behind when it stops.</summary>
    /// <returns>The double.</returns>
    public static UnixSocketAgentDouble Stale()
    {
        return new UnixSocketAgentDouble(bind: true, listen: false);
    }

    /// <summary>An agent that accepts a connection, reads what is sent, and never answers.</summary>
    /// <returns>The double.</returns>
    public static UnixSocketAgentDouble AcceptAndHold()
    {
        return new UnixSocketAgentDouble(bind: true, listen: true);
    }

    /// <summary>Stops serving, closes every connection and removes the socket file.</summary>
    public void Dispose()
    {
        _stop.Cancel();
        _bound?.Dispose();

        lock (_accepted)
        {
            foreach (var socket in _accepted)
            {
                socket.Dispose();
            }
        }

        _stop.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    /// <summary>Accepts connections until the double is disposed.</summary>
    /// <param name="listener">The listening socket.</param>
    /// <returns>A task that ends when serving stops.</returns>
    private async Task AcceptLoopAsync(Socket listener)
    {
        try
        {
            while (true)
            {
                var connection = await listener.AcceptAsync(_stop.Token);

                lock (_accepted)
                {
                    _accepted.Add(connection);
                }

                _ = ReadLoopAsync(connection);
            }
        }
        catch (OperationCanceledException)
        {
            // Disposed: the loop ends.
        }
        catch (ObjectDisposedException)
        {
            // Disposed while accepting: the loop ends.
        }
        catch (SocketException)
        {
            // The listener was closed under the accept: the loop ends.
        }
    }

    /// <summary>Reads and discards what a client sends, signalling the first bytes.</summary>
    /// <param name="connection">The accepted connection.</param>
    /// <returns>A task that ends when the client or the double closes the connection.</returns>
    private async Task ReadLoopAsync(Socket connection)
    {
        var buffer = new byte[4096];

        try
        {
            while (await connection.ReceiveAsync(buffer, SocketFlags.None, _stop.Token) > 0)
            {
                _requestReceived.TrySetResult();
            }
        }
        catch (OperationCanceledException)
        {
            // Disposed: the loop ends.
        }
        catch (ObjectDisposedException)
        {
            // Disposed while reading: the loop ends.
        }
        catch (SocketException)
        {
            // The client reset the connection: the loop ends.
        }
    }
}
