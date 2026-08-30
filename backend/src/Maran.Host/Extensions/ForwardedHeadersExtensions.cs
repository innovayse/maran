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
/// only, so a header arriving from anywhere else is ignored, and a direct connection to port 5080
/// keeps its own address whatever it claims.
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
