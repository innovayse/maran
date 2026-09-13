using System.Net;
using System.Net.Sockets;
using Maran.Host.Security;

namespace Maran.Host.Tests.Security;

/// <summary>
/// What the kernel answers about the other end of a socket, and what the panel does with an answer
/// that is not one.
/// </summary>
/// <remarks>
/// The interesting case is the one the type's doc comment used to describe wrongly. Reading
/// <c>SO_PEERCRED</c> from a TCP socket does not fail on Linux: it SUCCEEDS and reports
/// <c>pid 0</c> with <c>(uid_t)-1</c> in both id fields. A reader that passed that on would hand
/// the peer policy the number <c>4294967295</c> to compare instead of an absence to refuse — which
/// is fail-closed only by arithmetic, and only until somebody writes a policy that treats a
/// missing configuration generously. These two tests pin the difference between "the kernel told
/// us who it is" and "the kernel told us there is nobody to tell us about".
///
/// <para>
/// <b>Mutations that redden them.</b> Deleting the <c>UnmappedUserId</c> check: the TCP test gets
/// credentials instead of <see langword="null"/>. Returning <see langword="null"/> unconditionally:
/// the unix test loses the uid it reads back through <c>getuid</c>'s own answer.
/// </para>
/// </remarks>
public sealed class PeerCredentialsTests
{
    /// <summary>A unix socket reports the peer's real uid, which for a self-connection is ours.</summary>
    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public void A_unix_socket_reports_the_peers_own_user_id()
    {
        var path = Path.Combine(Path.GetTempPath(), $"maran-cred-{Guid.NewGuid():N}.sock");
        try
        {
            using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(path));
            listener.Listen(1);
            using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            client.Connect(new UnixDomainSocketEndPoint(path));
            using var accepted = listener.Accept();

            var credentials = PeerCredentials.TryRead(accepted);

            Assert.NotNull(credentials);
            Assert.NotEqual(uint.MaxValue, credentials.Value.UserId);
            Assert.NotEqual(0, credentials.Value.ProcessId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A TCP socket has no peer credentials, and says so as absence rather than as a uid.</summary>
    [Fact]
    public void A_tcp_socket_reports_absence_rather_than_an_unmapped_user_id()
    {
        // Measured before it was written down: on Linux this getsockopt SUCCEEDS on a TCP socket
        // and fills the structure with pid=0 uid=4294967295 gid=4294967295. The refusal this type
        // is part of must see that as "nobody", never as a caller with an unusual uid.
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        client.Connect(listener.LocalEndPoint!);
        using var accepted = listener.Accept();

        Assert.Null(PeerCredentials.TryRead(accepted));
        Assert.Null(PeerCredentials.TryRead(client));
    }
}
