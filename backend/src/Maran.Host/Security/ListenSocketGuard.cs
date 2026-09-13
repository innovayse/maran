using Maran.Host.Configuration;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;

namespace Maran.Host.Security;

/// <summary>
/// Makes the panel's listening socket keep the promise the rest of the design rests on: that only
/// the reverse proxy can reach it.
/// </summary>
/// <remarks>
/// <b>Why anything has to run here.</b> Kestrel creates a unix socket with the ordinary file
/// creation mode and offers no option to choose one, so the mode depends entirely on the umask of
/// whatever started the panel: <c>0775</c> under this repository's development umask, and
/// <c>0750</c> under the shipped unit's <c>UMask=0027</c> (both measured — the second inside a
/// booted container on each family, with the real unit). Group write is what nginx needs and
/// neither default grants it, and a panel started under a laxer umask is world-connectable
/// outright. Nothing else in the process settles that, so this does, immediately after the server
/// reports itself started.
///
/// <para>
/// <b>The window this leaves, and why it is closed elsewhere.</b> Between <c>bind(2)</c> and the
/// call below the socket carries whatever mode the umask gave it. That window is covered by the
/// socket's directory — <c>/run/maran-api</c>, built at <c>2710</c> owned <c>panel:&lt;web server
/// group&gt;</c> by <c>/etc/tmpfiles.d/maran-api.conf</c> before the unit starts — which no other
/// uid can traverse at any point. The directory is the boundary; this is the second lock on the
/// same door, and the reason a mistake in either one is not the end of the defence.
/// </para>
/// <para>
/// <b>Every failure here stops the panel.</b> A socket whose permissions could not be established
/// is a boundary nobody can vouch for, and this product's failure direction for a boundary is
/// closed and loud, never open and silent. The one thing that is merely a warning is running on
/// TCP at all — that is development's normal state, and, until an operator re-runs the installer,
/// an existing server's.
/// </para>
/// </remarks>
public sealed class ListenSocketGuard
{
    /// <summary>Marker separating the scheme from the path in Kestrel's unix-socket address form.</summary>
    private const string UnixAddressMarker = "://unix:";

    /// <summary>
    /// The mode the socket is narrowed to: the owner (the panel) and the group (nginx's) may use
    /// it, and nobody else may. Execute is meaningless on a socket and is not granted.
    /// </summary>
    private const UnixFileMode SocketMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite;

    /// <summary>Bits that would let any local process connect; none of them may survive.</summary>
    private const UnixFileMode OtherBits =
        UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    /// <summary>Pre-compiled log delegate for a socket that was successfully narrowed.</summary>
    private static readonly Action<ILogger, string, Exception?> LogSocketNarrowed =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(1, nameof(ListenSocketGuard)),
            "Panel socket {SocketPath} is restricted to its owner and the web server's group.");

    /// <summary>Pre-compiled log delegate for a panel that is listening on TCP instead.</summary>
    private static readonly Action<ILogger, Exception?> LogListeningOnTcp =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(2, nameof(ListenSocketGuard)),
            "The panel is not listening on a unix socket. Any local process can then reach it with "
            + "a loopback source address and choose the address the panel records and rate-limits "
            + "on. Re-run the installer to move the panel onto its socket.");

    /// <summary>Pre-compiled log delegate for a socket whose permissions could not be established.</summary>
    private static readonly Action<ILogger, string, Exception?> LogSocketNotSecured =
        LoggerMessage.Define<string>(
            LogLevel.Critical,
            new EventId(3, nameof(ListenSocketGuard)),
            "Panel socket {SocketPath} could not be restricted to its owner and the web server's "
            + "group, so it may be reachable by any local process. Shutting down.");

    /// <summary>Pre-compiled log delegate for a socket bound with no permitted peer configured.</summary>
    private static readonly Action<ILogger, string, Exception?> LogPeerUidMissing =
        LoggerMessage.Define<string>(
            LogLevel.Critical,
            new EventId(4, nameof(ListenSocketGuard)),
            "The panel is listening on a unix socket but {SettingName} is not set, so no caller can "
            + "be recognised as the reverse proxy and every request would be refused. Shutting down.");

    /// <summary>Pre-compiled log delegate for a panel that bound a socket and a network endpoint.</summary>
    private static readonly Action<ILogger, string, Exception?> LogMixedTransport =
        LoggerMessage.Define<string>(
            LogLevel.Critical,
            new EventId(5, nameof(ListenSocketGuard)),
            "The panel bound its unix socket AND the network endpoint {Address}. Every local "
            + "process can reach that endpoint and arrives with an address the panel trusts as its "
            + "reverse proxy, so the socket's peer check decides nothing. Shutting down.");

    /// <summary>The running server, which knows the endpoints it actually bound.</summary>
    private readonly IServer _server;

    /// <summary>The application lifetime, used to stop a panel that cannot vouch for its socket.</summary>
    private readonly IHostApplicationLifetime _lifetime;

    /// <summary>Settings naming the reverse proxy's uid.</summary>
    private readonly ReverseProxyOptions _options;

    /// <summary>Logger the outcome is recorded to.</summary>
    private readonly ILogger<ListenSocketGuard> _logger;

    /// <summary>Creates the guard.</summary>
    /// <param name="server">The running server, which knows the endpoints it bound.</param>
    /// <param name="lifetime">The application lifetime, used to stop the panel on failure.</param>
    /// <param name="options">Settings naming the reverse proxy's uid.</param>
    /// <param name="logger">Logger the outcome is recorded to.</param>
    public ListenSocketGuard(
        IServer server,
        IHostApplicationLifetime lifetime,
        IOptions<ReverseProxyOptions> options,
        ILogger<ListenSocketGuard> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _server = server;
        _lifetime = lifetime;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Narrows every unix socket the server bound, or stops the panel if it cannot.
    /// </summary>
    /// <remarks>
    /// Called once, from <c>ApplicationStarted</c>: that is the first moment the socket exists on
    /// disk and the first moment the bound addresses can be read back rather than assumed. Reading
    /// them back is deliberate — a socket path configured in one place and hardened from another
    /// is two values that can disagree, and this way there is only one.
    /// </remarks>
    public void Apply()
    {
        var addresses = _server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        var socketPaths = new List<string>();
        var networkAddresses = new List<string>();
        foreach (var address in addresses)
        {
            var socketPath = UnixSocketPathOf(address);
            if (socketPath is null)
            {
                networkAddresses.Add(address);
            }
            else
            {
                socketPaths.Add(socketPath);
            }
        }

        if (socketPaths.Count == 0)
        {
            LogListeningOnTcp(_logger, null);
            return;
        }

        // Narrowing comes FIRST, before either check that may stop the panel. A panel shutting
        // down is still serving for as long as the shutdown takes, and the socket it is serving on
        // must not be world-connectable for that window.
        foreach (var socketPath in socketPaths)
        {
            if (!TryRestrict(socketPath))
            {
                Stop();
                return;
            }
        }

        if (networkAddresses.Count > 0)
        {
            // The only silently-insecure state this design has, and it takes one stray entry in
            // ASPNETCORE_URLS to reach: Kestrel binds EVERY url it is given, so a panel can be on
            // its socket and on loopback TCP at once. The socket half then looks perfectly healthy
            // — narrowed, peer-checked, satisfied — while the TCP half hands any local process the
            // loopback source address that AddPanelForwardedHeaders trusts, which is the whole
            // flaw this change exists to close. Refused rather than warned about: every other
            // failure direction here is closed and loud, and this is the one that would not be.
            LogMixedTransport(_logger, string.Join(", ", networkAddresses), null);
            Stop();
            return;
        }

        if (_options.PeerUid is null)
        {
            // Without this the panel would run on a socket with no way to tell its proxy from
            // anyone else who reached it. PanelPeerAddressMiddleware refuses such a caller for the
            // length of the shutdown, and this is what makes the shutdown happen at all.
            LogPeerUidMissing(
                _logger,
                $"{ReverseProxyOptions.SectionName}:{nameof(ReverseProxyOptions.PeerUid)}",
                null);
            Stop();
        }
    }

    /// <summary>Stops the panel, and makes the process exit say that it stopped for a reason.</summary>
    /// <remarks>
    /// <see cref="IHostApplicationLifetime.StopApplication"/> on its own is a graceful shutdown, so
    /// the process exits <c>0</c> and systemd records <c>inactive (dead)</c> — indistinguishable
    /// from an operator having stopped the panel on purpose, and, under the unit's
    /// <c>Restart=on-failure</c>, not a reason to try again. Setting the exit code first turns that
    /// into <c>failed</c>: the unit is restarted, hits the same refusal, and after
    /// <c>StartLimitBurst</c> attempts stays in a state <c>systemctl status</c> reports as a
    /// failure. This product's failure direction for a boundary is closed AND loud, and the log
    /// line above should not be the only thing that says so.
    /// </remarks>
    private void Stop()
    {
        Environment.ExitCode = 1;
        _lifetime.StopApplication();
    }

    /// <summary>Extracts the socket path from a Kestrel address, or null when it is not a unix one.</summary>
    /// <param name="address">One address the server reports as bound.</param>
    /// <returns>The socket's filesystem path, or null for a TCP address.</returns>
    private static string? UnixSocketPathOf(string address)
    {
        var marker = address.IndexOf(UnixAddressMarker, StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return null;
        }

        var path = address[(marker + UnixAddressMarker.Length)..].TrimEnd('/');
        return path.Length == 0 ? null : path;
    }

    /// <summary>Narrows one socket and verifies the result, reporting failure rather than throwing.</summary>
    /// <param name="socketPath">Filesystem path of the socket to restrict.</param>
    /// <returns>True when the socket is provably unreachable by other local users.</returns>
    private bool TryRestrict(string socketPath)
    {
        if (OperatingSystem.IsWindows())
        {
            // Unreachable in production — the panel ships on Linux only — but the framework's
            // file-mode API is unsupported there and an unguarded call would not compile.
            LogSocketNotSecured(_logger, socketPath, null);
            return false;
        }

        try
        {
            File.SetUnixFileMode(socketPath, SocketMode);

            // Read back rather than trust the write: the whole boundary is these bits, and an
            // assertion that never runs is the shape of defect this change exists to remove.
            if ((File.GetUnixFileMode(socketPath) & OtherBits) != 0)
            {
                LogSocketNotSecured(_logger, socketPath, null);
                return false;
            }
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            LogSocketNotSecured(_logger, socketPath, failure);
            return false;
        }

        LogSocketNarrowed(_logger, socketPath, null);
        return true;
    }
}
