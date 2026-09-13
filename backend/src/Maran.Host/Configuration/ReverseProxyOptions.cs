namespace Maran.Host.Configuration;

/// <summary>
/// Which local peer the panel accepts as its reverse proxy. Bound from the <c>ReverseProxy</c>
/// configuration section, written by <c>installer/lib/60-config.sh</c> into
/// <c>/etc/maran/panel.env</c>.
/// </summary>
/// <remarks>
/// The panel listens on a unix domain socket, so the peer of every connection is a unix uid the
/// kernel reports rather than an address the caller can choose. This is the one uid that may use
/// it — nginx's, and nothing else on the box.
///
/// <para>
/// <b>Absent is not "anyone".</b> <see cref="PeerUid"/> is nullable and there is no default,
/// because there is no uid that is safe to guess: an unconfigured panel refuses every socket
/// caller rather than accepting one (<see cref="Maran.Host.Security.PanelPeerPolicy"/>). This
/// area has already produced one defect where missing configuration read as "trust everyone" —
/// an empty <c>KnownProxies</c> list skips the known-peer check entirely — and the shape is not
/// repeated here.
/// </para>
/// <para>
/// It is deliberately not <c>[Required]</c>. Development runs the panel over TCP, where the value
/// is never consulted; demanding it there would fail a boot that has nothing wrong with it. The
/// refusal happens where it means something: at the socket.
/// </para>
/// </remarks>
public sealed class ReverseProxyOptions
{
    /// <summary>Configuration section this type binds from.</summary>
    public const string SectionName = "ReverseProxy";

    /// <summary>
    /// Unix uid of the web server process permitted to connect to the panel's listening socket,
    /// or <see langword="null"/> when none is configured — in which case no peer is permitted.
    /// </summary>
    public uint? PeerUid { get; set; }
}
