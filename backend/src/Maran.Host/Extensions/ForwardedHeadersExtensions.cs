using Microsoft.AspNetCore.HttpOverrides;

namespace Maran.Host.Extensions;

/// <summary>
/// Teaches the panel to read the caller's real address from the reverse proxy in front of it.
/// </summary>
/// <remarks>
/// The panel is always behind nginx on a real server — that is the only supported deployment, and
/// the installer writes the vhost. nginx sends <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c>;
/// until this existed, nothing read them, so every request appeared to come from <c>127.0.0.1</c>.
/// Three things were quietly wrong because of it:
/// <list type="bullet">
/// <item>the login rate limiter partitions on the caller's address, so in production every user on
/// earth shared one budget of five attempts — no protection against an attacker, and a denial of
/// service against everyone else, since five wrong passwords from anybody locked out the panel;</item>
/// <item>the audit journal records "who, when, what, from where" with the where always loopback;</item>
/// <item>the session list showed every device as signed in from the server itself.</item>
/// </list>
///
/// <para>
/// <b>Only the proxy is trusted.</b> <c>X-Forwarded-For</c> is a header the client sends, so
/// honouring it unconditionally would hand the rate limiter a key the caller chooses — the exact
/// defect that made the limiter useless before, in a new place. The known-proxy list is loopback
/// only, so a header arriving from a REMOTE peer is ignored.
/// </para>
/// <para>
/// <b>Loopback is not by itself the boundary, and this file does not pretend it is.</b> Any local
/// process connects with a source address of <c>127.0.0.1</c>, which is exactly what the trusted
/// list contains — so on a TCP listener a customer's cron entry or PHP script chooses the address
/// the panel records and rate-limits on. What closes that is the transport, not this options
/// block: the panel listens on <c>/run/maran-api/api.sock</c> in a directory only nginx's group
/// may traverse, and <c>PanelPeerAddressMiddleware</c> stamps <c>127.0.0.1</c> onto a connection
/// only after the kernel has confirmed the peer is the web server. **The loopback entries below
/// are therefore no longer a statement about who may connect; they are the token this panel's own
/// peer check hands forward, and they mean what they say only because that check runs first.**
/// A panel still bound to TCP — development, or a server whose installer has not been re-run —
/// keeps the old exposure, and <c>ListenSocketGuard</c> says so at warning level on every boot.
/// See <c>docs/superpowers/notes/2026-09-03-panel-socket-threat-note.md</c>.
/// </para>
/// <para>
/// <b>Both lists are cleared and re-added deliberately.</b> In ASP.NET Core the known-peer check
/// runs only when at least one of <see cref="ForwardedHeadersOptions.KnownProxies"/> or
/// <see cref="ForwardedHeadersOptions.KnownNetworks"/> is non-empty, so leaving BOTH empty does
/// not mean "trust nobody" — it means the check is skipped and every peer is trusted. The two
/// <c>Add</c> calls below are therefore load-bearing, and deleting them fails OPEN.
/// </para>
/// </remarks>
public static class ForwardedHeadersExtensions
{
    /// <summary>Registers forwarded-header processing, trusting the local reverse proxy only.</summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddPanelForwardedHeaders(this IServiceCollection services)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            // One hop: nginx on this machine. More would mean trusting whatever nginx was given.
            options.ForwardLimit = 1;

            // The defaults already contain the loopback addresses; clearing and re-adding them
            // states the trust boundary in one place instead of relying on a framework default
            // that a later ASP.NET version could widen.
            options.KnownProxies.Clear();
            options.KnownNetworks.Clear();
            options.KnownProxies.Add(System.Net.IPAddress.Loopback);
            options.KnownProxies.Add(System.Net.IPAddress.IPv6Loopback);
        });

        return services;
    }
}
