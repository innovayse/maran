using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.SitesService;

/// <summary>
/// What a site IS, as opposed to which site it is: the facts every re-render of its vhost needs.
/// </summary>
/// <remarks>
/// The agent stores no such record — the vhost on disk is a rendering of the panel's database, not
/// a second copy of it — so every operation that re-renders a vhost carries these facts with it.
/// Omitting them is refused by the agent rather than defaulted, because a defaulted backend would
/// silently replace a PHP site's vhost with a static one.
/// </remarks>
/// <param name="Aliases">Additional hostnames served by the same site.</param>
/// <param name="Backend">Which backend serves the site's content.</param>
/// <param name="PhpVersion">The bound PHP version; required when <paramref name="Backend"/> is PHP.</param>
/// <param name="ProxyUpstream">The upstream to forward to; required when the backend is a reverse proxy.</param>
/// <param name="Node">
/// What a Node backend runs; <see cref="SiteNodeApp.None"/> for any other backend. Required for the
/// reason <paramref name="Options"/> is: a re-render without it would drop a Node site's unit.
/// </param>
/// <param name="WebServer">
/// The web server whose vhost is rendered. Required for the reason <paramref name="Options"/> is: the wire reads
/// an absent one as nginx, so a re-render without it would write an nginx vhost for a site Apache serves.
/// </param>
/// <param name="HasCertificate">
/// Whether a TLS certificate is currently installed, so the re-rendered vhost keeps its TLS block.
/// The panel knows this and the renderer must not guess: a guess drops a live site back to plain
/// HTTP on the next unrelated edit.
/// </param>
/// <param name="Options">
/// The site's per-site options. Required rather than optional for the reason <see cref="HasCertificate"/>
/// is: a re-render that omits them drops the site's redirects, credentials and IP rules, so a caller
/// without any says <see cref="SiteOptionsDescriptor.None"/> out loud.
/// </param>
public sealed record SiteDescriptor(
    IReadOnlyList<string> Aliases,
    SiteBackendKind Backend,
    string PhpVersion,
    string ProxyUpstream,
    SiteNodeApp Node,
    SiteWebServer WebServer,
    bool HasCertificate,
    SiteOptionsDescriptor Options)
{
    /// <summary>Converts this descriptor into the wire message the agent's rpcs require.</summary>
    /// <returns>The populated <see cref="SiteSpec"/>.</returns>
    internal SiteSpec ToWire()
    {
        var spec = new SiteSpec
        {
            BackendType = ToWireBackend(Backend),
            PhpVersion = PhpVersion,
            ProxyUpstream = ProxyUpstream,
            NodeVersion = Node.Version,
            AppPort = Node.Port,
            StartCommand = Node.StartCommand,
            WebServer = ToWireWebServer(WebServer),
            HasCertificate = HasCertificate,
            Options = Options.ToWire(),
        };
        spec.Aliases.AddRange(Aliases);

        return spec;
    }

    /// <summary>Maps the panel's backend kind onto its wire counterpart.</summary>
    /// <param name="backend">The panel-side kind.</param>
    /// <returns>The wire value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown for a kind this mapping does not know. That is a defect of the panel (a member added to
    /// <see cref="SiteBackendKind"/> without its arm here), and it must not travel as the wire's
    /// unspecified value for the agent to refuse at a distance.
    /// </exception>
    internal static SiteBackendType ToWireBackend(SiteBackendKind backend)
    {
        return backend switch
        {
            SiteBackendKind.Static => SiteBackendType.Static,
            SiteBackendKind.Php => SiteBackendType.Php,
            SiteBackendKind.ReverseProxy => SiteBackendType.ReverseProxy,
            SiteBackendKind.Node => SiteBackendType.Node,
            _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, "Unknown site backend kind."),
        };
    }

    /// <summary>Maps the panel's web server onto its wire counterpart.</summary>
    /// <param name="webServer">The panel-side web server.</param>
    /// <returns>The wire value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown for a web server this mapping does not know, for the reason <see cref="ToWireBackend"/> gives.
    /// </exception>
    internal static Maran.Agent.V1.SiteWebServer ToWireWebServer(SiteWebServer webServer)
    {
        return webServer switch
        {
            SiteWebServer.Nginx => Maran.Agent.V1.SiteWebServer.Nginx,
            SiteWebServer.Apache => Maran.Agent.V1.SiteWebServer.Apache,
            _ => throw new ArgumentOutOfRangeException(nameof(webServer), webServer, "Unknown site web server."),
        };
    }
}
