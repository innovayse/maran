using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Maran.Host.Security;

/// <summary>
/// The kernel's own answer to "which process is on the other end of this unix socket".
/// </summary>
/// <remarks>
/// This is Linux's <c>struct ucred</c>, read with <c>SO_PEERCRED</c>. The kernel fills it in at
/// <c>connect(2)</c> time from the connecting process's real credentials: unlike anything carried
/// in the request — an address, a header, a token — it cannot be set by the caller. That is the
/// whole reason the panel's trust boundary is a socket rather than a loopback address.
///
/// <para>
/// The agent reads the same fact from the other side of the same relationship
/// (<c>agent/crates/agent/src/peercred/peer_guard.rs</c>), and treats credentials being absent as
/// a denial rather than as a reason to fall back to something weaker. <see cref="TryRead"/>
/// returns <see langword="null"/> for exactly that reason: there is no second-best answer to
/// substitute, so callers are given nothing to mistake for one.
/// </para>
/// </remarks>
public readonly record struct PeerCredentials
{
    /// <summary>Socket option level for socket-level options — Linux's <c>SOL_SOCKET</c>.</summary>
    private const int SolSocket = 1;

    /// <summary>Socket option name for the peer's credentials — Linux's <c>SO_PEERCRED</c>.</summary>
    private const int SoPeerCred = 17;

    /// <summary>Size of Linux's <c>struct ucred</c>: three 32-bit fields.</summary>
    private const int UcredSize = 12;

    /// <summary>
    /// The kernel's spelling of "there is nobody on the other end of this": <c>(uid_t)-1</c>.
    /// </summary>
    /// <remarks>
    /// Not a uid any account can hold — <c>useradd</c> refuses it and the kernel reserves it as the
    /// "unmapped" identity — so treating it as absence can never refuse a real caller.
    /// </remarks>
    private const uint UnmappedUserId = uint.MaxValue;

    /// <summary>Process id of the peer, as recorded when it connected.</summary>
    public int ProcessId { get; init; }

    /// <summary>Unix user id of the peer, as recorded when it connected.</summary>
    public uint UserId { get; init; }

    /// <summary>Unix group id of the peer, as recorded when it connected.</summary>
    public uint GroupId { get; init; }

    /// <summary>
    /// Reads the peer's credentials from a connected unix socket, or <see langword="null"/> when
    /// the socket cannot supply them.
    /// </summary>
    /// <param name="socket">The connected socket to interrogate.</param>
    /// <returns>The peer's credentials, or <see langword="null"/> when they are unavailable.</returns>
    /// <remarks>
    /// There are two ways a socket can have no credentials to give, and both are reported the same
    /// way — as <see langword="null"/>, so the caller decides what absence means, with no
    /// second-best answer to mistake for one.
    ///
    /// <para>
    /// The first is a failed option read, which surfaces as a <see cref="SocketException"/> (or a
    /// <see cref="PlatformNotSupportedException"/> on a platform without <c>SO_PEERCRED</c>). The
    /// second is the one that is easy to get wrong, and this comment used to: <b>on Linux the
    /// option read SUCCEEDS on a TCP socket</b> and fills the structure with <c>pid 0</c> and
    /// <c>(uid_t)-1</c>. Measured, on both ends of a loopback TCP connection:
    /// <c>getsockopt(SO_PEERCRED) SUCCEEDED -&gt; pid=0 uid=4294967295 gid=4294967295</c>.
    /// Returned as it stands, that would hand the peer policy a uid to compare instead of an
    /// absence to refuse, so <see cref="UnmappedUserId"/> is turned back into absence below.
    /// </para>
    /// </remarks>
    public static PeerCredentials? TryRead(Socket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);

        var buffer = new byte[UcredSize];
        int written;
        try
        {
            written = socket.GetRawSocketOption(SolSocket, SoPeerCred, buffer);
        }
        catch (SocketException)
        {
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }

        if (written != UcredSize)
        {
            return null;
        }

        var userId = MemoryMarshal.Read<uint>(buffer.AsSpan(4, 4));
        if (userId == UnmappedUserId)
        {
            // The read succeeded and said there is nobody there. See the remarks: this is what a
            // TCP socket answers on Linux, and absence must not arrive at the policy as a uid.
            return null;
        }

        return new PeerCredentials
        {
            ProcessId = MemoryMarshal.Read<int>(buffer.AsSpan(0, 4)),
            UserId = userId,
            GroupId = MemoryMarshal.Read<uint>(buffer.AsSpan(8, 4)),
        };
    }
}
