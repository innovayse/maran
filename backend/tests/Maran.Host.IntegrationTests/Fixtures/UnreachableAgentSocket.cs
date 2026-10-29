using System.Net.Sockets;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// A unix-socket path that no agent answers on, in one of the two shapes an outage really has: a path
/// that does not exist (the agent was never started, or was removed), and a path that exists but is
/// bound by nothing that listens (the agent crashed and left its socket file behind).
/// </summary>
/// <remarks>
/// The outage tests dial these through the REAL gRPC channel, because the defect behind issue #87 was a
/// retry and mapping rule written against an exception type that no test ever threw and the transport
/// never produced: every fake raised a hand-made exception, and the real refusal went unmapped. Nothing
/// here replaces the transport; it only chooses what is, or is not, at the other end of the path. The
/// directory is private to the instance and removed on disposal, and the path stays well under the
/// 108-byte limit of a unix socket address.
/// </remarks>
public sealed class UnreachableAgentSocket : IDisposable
{
    /// <summary>The shape name of a path with nothing behind it.</summary>
    public const string MissingShape = "missing";

    /// <summary>The shape name of a path bound by a socket that never listens.</summary>
    public const string StaleShape = "stale";

    /// <summary>The private directory holding the socket file, removed on disposal.</summary>
    private readonly string _directory;

    /// <summary>The bound but never listening socket of the stale shape, or null for the missing one.</summary>
    private readonly Socket? _bound;

    /// <summary>The path to hand to <c>Agent:SocketPath</c>.</summary>
    public string SocketPath { get; }

    /// <summary>Creates the instance around its directory and, for the stale shape, its bound socket.</summary>
    /// <param name="directory">The private directory.</param>
    /// <param name="socketPath">The path the agent client is told to dial.</param>
    /// <param name="bound">The bound socket, or null.</param>
    private UnreachableAgentSocket(string directory, string socketPath, Socket? bound)
    {
        _directory = directory;
        SocketPath = socketPath;
        _bound = bound;
    }

    /// <summary>Builds the shape the theory row names.</summary>
    /// <param name="shape"><see cref="MissingShape"/> or <see cref="StaleShape"/>.</param>
    /// <returns>The socket the caller disposes.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The shape is neither of the two.</exception>
    public static UnreachableAgentSocket Of(string shape)
    {
        return shape switch
        {
            MissingShape => Missing(),
            StaleShape => Stale(),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unknown socket shape."),
        };
    }

    /// <summary>A path inside a directory that holds nothing: connecting fails with "no such file".</summary>
    /// <returns>The socket the caller disposes.</returns>
    public static UnreachableAgentSocket Missing()
    {
        var directory = NewDirectory();

        return new UnreachableAgentSocket(directory, Path.Combine(directory, "agent.sock"), null);
    }

    /// <summary>A socket file bound and never listened on: connecting is refused.</summary>
    /// <returns>The socket the caller disposes.</returns>
    public static UnreachableAgentSocket Stale()
    {
        var directory = NewDirectory();
        var path = Path.Combine(directory, "agent.sock");
        var bound = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        bound.Bind(new UnixDomainSocketEndPoint(path));

        return new UnreachableAgentSocket(directory, path, bound);
    }

    /// <summary>Closes the stale socket, if any, and removes the directory.</summary>
    public void Dispose()
    {
        _bound?.Dispose();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A temp directory that outlives its test is clutter, never a failure of the test.
        }
    }

    /// <summary>Creates a short private directory under the temp root.</summary>
    /// <returns>The directory's path.</returns>
    private static string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mo-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(directory);

        return directory;
    }
}
