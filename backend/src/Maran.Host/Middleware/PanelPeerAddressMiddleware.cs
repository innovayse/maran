using System.Net;
using Maran.Host.Configuration;
using Maran.Host.Security;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.Extensions.Options;

namespace Maran.Host.Middleware;

/// <summary>
/// Turns the kernel's answer to "who connected" into the address the rest of the pipeline reasons
/// about, and refuses any local caller that is not the reverse proxy.
/// </summary>
/// <remarks>
/// <b>Why this exists at all.</b> The panel listens on a unix domain socket, so Kestrel reports
/// <see cref="ConnectionInfo.RemoteIpAddress"/> as <see langword="null"/> — a unix endpoint has no
/// address. <c>ForwardedHeadersMiddleware</c> decides whether to honour <c>X-Forwarded-For</c> by
/// matching that address against its known-proxy list, and .NET's implementation carries an
/// explicit carve-out allowing a null address for servers that cannot report one. **Measured on
/// .NET 9: a null address does not fail that check, it skips it — the header is honoured from any
/// peer, with the known-proxy list bypassed entirely.** Moving to a socket without this component
/// in front would therefore have *removed* the last address check while looking like a hardening
/// change. The measurement is recorded in
/// <c>docs/superpowers/notes/2026-09-03-panel-socket-threat-note.md</c>.
///
/// <para>
/// <b>What it does.</b> For a connection with no address — that is, a unix socket — it reads the
/// peer's uid from <c>SO_PEERCRED</c>. A permitted peer (nginx, and only nginx) has its connection
/// stamped as <see cref="IPAddress.Loopback"/>, which is precisely what
/// <c>AddPanelForwardedHeaders</c> trusts, so the framework middleware then runs its real
/// known-proxy comparison and honours the header. Any other peer is refused outright, before the
/// header is looked at.
/// </para>
/// <para>
/// <b>What it does NOT do, and must not.</b> It never overwrites an address Kestrel already knows.
/// A TCP connection — development, and every server whose installer has not been re-run — keeps
/// the peer Kestrel reported, so the trust decision there is unchanged and this component is
/// inert. Widening it to "always stamp loopback" would hand every TCP caller the proxy's trust.
/// </para>
/// <para>
/// <b>It stands aside for exactly one shape, and that shape is not a connection.</b> A request
/// with no address, NO SOCKET and no configured peer uid is the in-memory test server and nothing
/// else: it presents neither of the two things this component reads, so refusing there would make
/// every host test in the repository fail for a reason that has nothing to do with the panel. A
/// connection that has a socket is always decided here, including when no peer uid is configured —
/// an unconfigured <c>PanelPeerPolicy</c> permits nobody, so it is refused. That case is narrow but
/// real: <c>ListenSocketGuard</c> runs at <c>ApplicationStarted</c>, which is after Kestrel is
/// accepting, so a socket-bound panel with no peer uid is briefly serving between the first accept
/// and the shutdown the guard asks for. It serves <c>403</c> for that window rather than standing
/// aside in it.
/// </para>
/// <para>
/// <b>Where the boundary really is.</b> Not here. A customer's process is stopped by the socket's
/// directory — <c>/run/maran-api</c>, built <c>2710 panel:&lt;web server group&gt;</c> by
/// <c>/etc/tmpfiles.d/maran-api.conf</c> before the unit starts — before it can <c>connect(2)</c>
/// at all; this check is the second stop, and the only one a backend test can drive. It is written
/// so that the first stop being wrong is not the end of the defence.
/// </para>
/// </remarks>
public sealed class PanelPeerAddressMiddleware
{
    /// <summary>
    /// Pre-compiled log delegate for a refused peer. Source-generated because a machine whose
    /// socket permissions have been widened could hit this on every request.
    /// </summary>
    private static readonly Action<ILogger, uint, int, Exception?> LogRefusedPeer =
        LoggerMessage.Define<uint, int>(
            LogLevel.Warning,
            new EventId(1, nameof(PanelPeerAddressMiddleware)),
            "Refused a connection to the panel socket from uid {PeerUid} (pid {PeerProcessId}): "
            + "only the configured reverse-proxy uid may use it.");

    /// <summary>Pre-compiled log delegate for a socket peer whose credentials could not be read.</summary>
    private static readonly Action<ILogger, Exception?> LogUnknownPeer =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(2, nameof(PanelPeerAddressMiddleware)),
            "Refused a connection to the panel socket whose peer credentials could not be read.");

    /// <summary>The next component in the pipeline.</summary>
    private readonly RequestDelegate _next;

    /// <summary>Which uid may use the panel's socket.</summary>
    private readonly PanelPeerPolicy _policy;

    /// <summary>Logger refusals are recorded to.</summary>
    private readonly ILogger<PanelPeerAddressMiddleware> _logger;

    /// <summary>Creates the middleware.</summary>
    /// <param name="next">The next component in the pipeline.</param>
    /// <param name="options">Settings naming the reverse proxy's uid.</param>
    /// <param name="logger">Logger refusals are recorded to.</param>
    public PanelPeerAddressMiddleware(
        RequestDelegate next,
        IOptions<ReverseProxyOptions> options,
        ILogger<PanelPeerAddressMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _next = next;
        _policy = new PanelPeerPolicy(options.Value.PeerUid);
        _logger = logger;
    }

    /// <summary>Applies the peer policy, then hands the request on.</summary>
    /// <param name="context">The current HTTP request.</param>
    /// <returns>Resolves once the request has been handled or refused.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Connection.RemoteIpAddress is not null)
        {
            // Kestrel already knows the peer: a TCP connection. Its address is the trust input,
            // exactly as before this file existed.
            await _next(context);
            return;
        }

        var socket = context.Features.Get<IConnectionSocketFeature>()?.Socket;
        if (socket is null && !_policy.IsConfigured)
        {
            // No address, no socket and no policy: the in-memory test server, and nothing else the
            // panel can be running on. See the remarks for why standing aside here is the right
            // answer and refusing is not. A connection that HAS a socket does not come here even
            // when no uid is configured — that is the shutdown window, and it is refused below.
            await _next(context);
            return;
        }

        var credentials = socket is null ? null : PeerCredentials.TryRead(socket);
        if (credentials is null)
        {
            // Absent credentials are a denial, not a reason to fall back to something weaker —
            // the same choice the agent's peer guard makes.
            LogUnknownPeer(_logger, null);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (!_policy.Permits(credentials.Value.UserId))
        {
            LogRefusedPeer(_logger, credentials.Value.UserId, credentials.Value.ProcessId, null);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        // The peer IS the reverse proxy, so give the forwarded-header machinery the address it was
        // configured to trust. Not a cosmetic fill-in: it is what makes the known-proxy comparison
        // run at all, and a request that reaches here has passed a check the caller cannot forge.
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Connection.RemotePort = 0;

        await _next(context);
    }
}
