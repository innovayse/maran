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
/// <param name="HasCertificate">
/// Whether a TLS certificate is currently installed, so the re-rendered vhost keeps its TLS block.
/// The panel knows this and the renderer must not guess: a guess drops a live site back to plain
/// HTTP on the next unrelated edit.
/// </param>
public sealed record SiteDescriptor(
    IReadOnlyList<string> Aliases,
    SiteBackendKind Backend,
    string PhpVersion,
    string ProxyUpstream,
    bool HasCertificate)
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
            HasCertificate = HasCertificate,
        };
        spec.Aliases.AddRange(Aliases);

        return spec;
    }

    /// <summary>Maps the panel's backend kind onto its wire counterpart.</summary>
    /// <param name="backend">The panel-side kind.</param>
    /// <returns>
    /// The wire value, or <see cref="SiteBackendType.Unspecified"/> for a kind this mapping does not
    /// know — which the agent refuses as invalid input rather than acting on a default.
    /// </returns>
    internal static SiteBackendType ToWireBackend(SiteBackendKind backend)
    {
        return backend switch
        {
            SiteBackendKind.Static => SiteBackendType.Static,
            SiteBackendKind.Php => SiteBackendType.Php,
            SiteBackendKind.ReverseProxy => SiteBackendType.ReverseProxy,
            _ => SiteBackendType.Unspecified,
        };
    }
}
