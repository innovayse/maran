using System.Net;
using System.Net.Sockets;
using Maran.Host.Configuration;
using Maran.Host.Extensions;
using Maran.Host.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Maran.Host.Tests.Middleware;

/// <summary>
/// The panel's trust boundary as the kernel enforces it: whether a caller reaching the listening
/// socket may choose the address the panel records, and who may reach the socket at all.
/// </summary>
/// <remarks>
/// These run a REAL Kestrel over a REAL unix domain socket, not the in-memory test server, because
/// the proposition under test is a property of the transport. The in-memory server has no sockets,
/// so it can neither produce the null <c>RemoteIpAddress</c> that makes this component necessary
/// nor carry the peer credentials that make it work — a test built on it would agree with any
/// implementation.
///
/// <para>
/// <b>What each test exercises, and what it cannot see.</b>
/// </para>
/// <para>
/// The permitted-peer test proves the whole proxy path end to end: the peer check passes, the
/// address is stamped, and the framework's known-proxy comparison runs and matches. It asserts
/// <c>X-Original-For</c> as well as the address, and that second assertion is the load-bearing
/// one — with the stamping deleted the address assertion alone still passes, because .NET's
/// forwarded-header middleware honours the header outright when there is no address to compare
/// (measured; see the threat note). <c>X-Original-For</c> is populated only when the comparison
/// actually ran, so it is the only thing here that can tell "trusted" apart from "unchecked".
/// </para>
/// <para>
/// The refused-peer test is the boundary itself. It cannot connect as a second uid — a test
/// process is one user — so it moves the allow-list instead of the caller, which exercises exactly
/// the same code path with the same kernel-supplied credential. What it therefore does NOT prove
/// is the production directory and socket permissions. Those are host facts: the polygon builds
/// the directory with the installer's own code and this family's real <c>systemd-tmpfiles</c> and
/// stats it, and the connect itself — the web server's uid admitted, a customer's uid refused —
/// was measured on booted systemd on both families and recorded in the panel socket threat note.
/// </para>
/// <para>
/// The socket-mode test covers the second lock. The TCP test covers the promise that this changed
/// nothing for a panel that is not on a socket. Three cover the states the panel must refuse to be
/// in or to serve: a socket with no peer uid must not run at all; a socket bound beside a network
/// endpoint must not run at all, because the network half restores the flaw in full while the
/// socket half looks healthy; and while either shutdown is in progress a socket caller is refused
/// rather than waved past.
/// </para>
/// <para>
/// <b>Mutations that redden them.</b> Deleting <c>UsePanelPeerAddress()</c> from the pipeline, or
/// removing the policy check inside it: refused-peer goes 200. Removing the
/// <c>RemoteIpAddress</c> stamp: permitted-peer loses <c>X-Original-For</c>. Removing the
/// <c>File.SetUnixFileMode</c> call in <c>ListenSocketGuard</c>: socket-mode sees the mode the
/// umask gave it. Removing that guard's missing-uid check, or its mixed-transport check: the panel
/// keeps serving in a state it must not. Restoring the middleware's old "stand aside whenever no
/// uid is configured" branch: the shutdown-window test goes 200. Applying the peer check to
/// connections Kestrel already has an address for: the TCP test goes 403.
/// </para>
/// </remarks>
public sealed class PanelPeerAddressTests
{
    /// <summary>The address nginx would forward for the real caller.</summary>
    private const string ClientAddress = "203.0.113.7";

    /// <summary>What the framework records as the peer it checked, once the comparison has run.</summary>
    private const string ProxyOriginalFor = "127.0.0.1:0";

    /// <summary>A forwarded address from the permitted socket peer is the one the panel records.</summary>
    /// <returns>Resolves once the assertion has run.</returns>
    [Fact]
    public async Task The_address_the_permitted_peer_forwards_is_the_one_the_panel_records()
    {
        var socketPath = TemporarySocketPath();
        await using var app = await StartOnSocketAsync(socketPath, CurrentUserId());
        using var client = ClientFor(socketPath);

        var seen = await GetWhoAmIAsync(client);

        Assert.Equal($"{ClientAddress}|{ProxyOriginalFor}", seen);
    }

    /// <summary>A socket caller whose uid is not the permitted one is refused outright.</summary>
    /// <returns>Resolves once the assertion has run.</returns>
    [Fact]
    public async Task A_caller_that_is_not_the_permitted_peer_is_refused()
    {
        // The kernel reports this process's own uid, and the allow-list names a different one, so
        // this is exactly what a customer's cron entry or PHP script would meet.
        var socketPath = TemporarySocketPath();
        await using var app = await StartOnSocketAsync(socketPath, CurrentUserId() + 1);
        using var client = ClientFor(socketPath);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/whoami");
        request.Headers.Add("X-Forwarded-For", ClientAddress);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>The socket the panel listens on carries no permissions for other local users.</summary>
    /// <returns>Resolves once the assertion has run.</returns>
    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public async Task The_listening_socket_is_closed_to_other_local_users()
    {
        // Kestrel creates a unix socket world-connectable and offers no option to choose the mode,
        // so a socket left as created is a door with the lock still in the box.
        var socketPath = TemporarySocketPath();
        await using var app = await StartOnSocketAsync(socketPath, CurrentUserId());

        var mode = File.GetUnixFileMode(socketPath);

        Assert.Equal(UnixFileMode.None, mode & UnixFileMode.OtherRead);
        Assert.Equal(UnixFileMode.None, mode & UnixFileMode.OtherWrite);
        Assert.Equal(UnixFileMode.None, mode & UnixFileMode.OtherExecute);
    }

    /// <summary>A TCP connection keeps the address Kestrel reported and is never refused.</summary>
    /// <returns>Resolves once the assertion has run.</returns>
    [Fact]
    public async Task A_connection_kestrel_already_has_an_address_for_is_left_alone()
    {
        // A panel on TCP — development, and every server whose installer has not been re-run —
        // must behave exactly as it did before this component existed. The allow-list here names a
        // uid nobody has, and it must still not matter.
        await using var app = await StartOnLoopbackAsync(CurrentUserId() + 1);
        using var client = new HttpClient { BaseAddress = BoundAddressOf(app) };

        var seen = await GetWhoAmIAsync(client);

        // The port is a real ephemeral one here rather than the socket path's synthetic zero, so
        // only the address half of X-Original-For can be spelled out — it still says the same
        // thing, that the known-proxy comparison ran against the peer Kestrel itself reported.
        Assert.StartsWith($"{ClientAddress}|127.0.0.1:", seen, StringComparison.Ordinal);
    }

    /// <summary>A panel bound to a socket with no permitted peer configured refuses to run.</summary>
    /// <returns>Resolves once the assertion has run.</returns>
    [Fact]
    public async Task A_socket_with_no_permitted_peer_configured_stops_the_panel()
    {
        // This is the line that makes PanelPeerAddressMiddleware's "inert when unconfigured"
        // branch safe. Delete the check in ListenSocketGuard and the panel keeps serving on a
        // socket with nothing able to tell its proxy from any other caller that reached it.
        var socketPath = TemporarySocketPath();
        await using var app = await StartOnSocketAsync(socketPath, peerUid: null);

        var stopped = app.Lifetime.ApplicationStopping.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));

        Assert.True(stopped, "the panel kept running on a socket it could not police");
    }

    /// <summary>A panel that bound a socket and a network endpoint at once refuses to run.</summary>
    /// <returns>Resolves once the assertion has run.</returns>
    [Fact]
    public async Task A_panel_that_also_bound_a_network_endpoint_refuses_to_run()
    {
        // One stray entry in ASPNETCORE_URLS reaches this state, and it is the only silently
        // insecure one this design has: the socket half looks healthy — narrowed, peer-checked,
        // satisfied — while the TCP half goes on handing every local process the loopback source
        // address the forwarded-header options trust. The permitted uid here is this process's
        // own, so nothing about the socket itself is what stops it.
        var socketPath = TemporarySocketPath();
        await using var app = Build(CurrentUserId(), kestrel =>
        {
            kestrel.ListenUnixSocket(socketPath);
            kestrel.Listen(IPAddress.Loopback, 0);
        });
        await app.StartAsync();

        var stopped = app.Lifetime.ApplicationStopping.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));

        Assert.True(stopped, "the panel kept running with a TCP listener beside its socket");
    }

    /// <summary>A socket caller is refused while no permitted peer is configured, not waved past.</summary>
    /// <returns>Resolves once the assertion has run.</returns>
    [Fact]
    public async Task A_socket_caller_is_refused_when_no_permitted_peer_is_configured()
    {
        // The state under test is real and narrow: ListenSocketGuard runs at ApplicationStarted,
        // which is after Kestrel is accepting, so a socket-bound panel with no peer uid serves for
        // the length of the shutdown it asks for. The guard is left out of the pipeline here
        // BECAUSE that is what holds the panel in that window long enough to send it a request —
        // with the guard in, the app is racing its own shutdown. What is asserted is the
        // middleware's answer, which is the thing that decides during the window.
        var socketPath = TemporarySocketPath();
        await using var app = Build(
            peerUid: null,
            kestrel =>
            {
                kestrel.ListenUnixSocket(socketPath);
            },
            withGuard: false);
        await app.StartAsync();
        using var client = ClientFor(socketPath);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/whoami");
        request.Headers.Add("X-Forwarded-For", ClientAddress);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>A socket path short enough for the kernel's 108-byte limit, in the temp directory.</summary>
    /// <returns>The path; no file exists there yet.</returns>
    private static string TemporarySocketPath()
    {
        return Path.Combine(Path.GetTempPath(), $"maran-peer-{Guid.NewGuid():N}.sock");
    }

    /// <summary>
    /// This process's own unix uid, read the way the middleware reads a caller's: from the kernel.
    /// </summary>
    /// <returns>The uid the kernel reports for this process.</returns>
    /// <remarks>
    /// A pair of sockets in this process is connected to itself, so the peer credentials on the
    /// client side describe this process. Read through the production type rather than a P/Invoke
    /// of <c>getuid</c>, so the test's idea of "who am I" comes from the same place the middleware's
    /// does — if that reader were broken, this would not quietly compensate for it.
    /// </remarks>
    private static uint CurrentUserId()
    {
        var path = TemporarySocketPath();
        try
        {
            using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(path));
            listener.Listen(1);

            using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            client.Connect(new UnixDomainSocketEndPoint(path));
            using var accepted = listener.Accept();

            var credentials = PeerCredentials.TryRead(client);
            Assert.NotNull(credentials);
            return credentials.Value.UserId;
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Starts the panel's address pipeline on a unix socket.</summary>
    /// <param name="socketPath">Where the socket is created.</param>
    /// <param name="peerUid">The uid the panel is configured to accept as its reverse proxy.</param>
    /// <returns>The started application; the caller disposes it.</returns>
    private static async Task<WebApplication> StartOnSocketAsync(string socketPath, uint? peerUid)
    {
        var app = Build(peerUid, kestrel =>
        {
            kestrel.ListenUnixSocket(socketPath);
        });

        await app.StartAsync();
        return app;
    }

    /// <summary>Starts the same pipeline on a loopback TCP port, as development runs it.</summary>
    /// <param name="peerUid">The uid the panel is configured to accept as its reverse proxy.</param>
    /// <returns>The started application; the caller disposes it.</returns>
    private static async Task<WebApplication> StartOnLoopbackAsync(uint? peerUid)
    {
        var app = Build(peerUid, kestrel =>
        {
            kestrel.Listen(IPAddress.Loopback, 0);
        });

        await app.StartAsync();
        return app;
    }

    /// <summary>
    /// Builds the host with the panel's OWN registrations and the panel's OWN pipeline order.
    /// </summary>
    /// <param name="peerUid">The uid the panel is configured to accept as its reverse proxy.</param>
    /// <param name="listen">How the server binds.</param>
    /// <param name="withGuard">Whether the startup guard is registered; false only where a test
    /// needs the panel held in a state the guard exists to end.</param>
    /// <returns>The built, not yet started, application.</returns>
    private static WebApplication Build(
        uint? peerUid,
        Action<KestrelServerOptions> listen,
        bool withGuard = true)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(listen);

        builder.Services.AddPanelForwardedHeaders();
        builder.Services.Configure<ReverseProxyOptions>(options =>
        {
            options.PeerUid = peerUid;
        });

        var app = builder.Build();

        if (withGuard)
        {
            app.UsePanelListenSocketGuard();
        }

        app.UsePanelPeerAddress();
        app.UseForwardedHeaders();

        app.MapGet("/whoami", (HttpContext context) =>
        {
            // Both halves matter. The address is the answer; X-Original-For is the evidence that
            // the framework's known-proxy comparison ran to produce it rather than being skipped.
            var address = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return Results.Text($"{address}|{context.Request.Headers["X-Original-For"]}");
        });

        return app;
    }

    /// <summary>A client that dials a unix socket instead of a TCP port.</summary>
    /// <param name="socketPath">The socket to dial.</param>
    /// <returns>The client; the caller disposes it.</returns>
    private static HttpClient ClientFor(string socketPath)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, token) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), token);
                return new NetworkStream(socket, ownsSocket: true);
            },
        };

        return new HttpClient(handler, disposeHandler: true) { BaseAddress = new Uri("http://localhost") };
    }

    /// <summary>The address a TCP-bound application actually listens on.</summary>
    /// <param name="app">The started application.</param>
    /// <returns>Its base address.</returns>
    private static Uri BoundAddressOf(WebApplication app)
    {
        return new Uri(Assert.Single(app.Urls));
    }

    /// <summary>Asks the probe endpoint who it thinks the caller is, forwarding a client address.</summary>
    /// <param name="client">The client to ask with.</param>
    /// <returns>The probe's answer.</returns>
    private static async Task<string> GetWhoAmIAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/whoami");
        request.Headers.Add("X-Forwarded-For", ClientAddress);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
}
