using Maran.Agent.Client.Services.SitesService;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Ssl.Mappers;

/// <summary>
/// Builds the <see cref="SiteDescriptor"/> the agent requires on every rpc that re-renders a vhost,
/// from the <see cref="SiteSnapshot"/> the Sites module handed over.
/// </summary>
/// <remarks>
/// It exists so that no handler in this module assembles a descriptor by hand. Installing a
/// certificate REWRITES the site's vhost, and every field the renderer needs is read from the
/// snapshot rather than written as a literal at a call site — a <c>false</c> typed in for
/// <c>HasCertificate</c>, or a defaulted <c>Static</c> backend, would silently rewrite a live PHP
/// site as a static one, and the mistake would look perfectly reasonable in a diff.
/// </remarks>
public static class SiteDescriptorMapper
{
    /// <summary>Describes a site to the agent, as it is about to be after this operation.</summary>
    /// <param name="site">The site's facts, from the module that owns them.</param>
    /// <param name="hasCertificate">
    /// Whether the vhost being written should carry a TLS block. Passed explicitly rather than read
    /// from the snapshot because this is the one operation that CHANGES it: an install must render
    /// TLS on a site whose stored flag is still false, and a removal must render plain HTTP on a site
    /// whose stored flag is still true.
    /// </param>
    /// <returns>The descriptor carrying that site's own facts.</returns>
    public static SiteDescriptor From(SiteSnapshot site, bool hasCertificate)
    {
        return new SiteDescriptor(
            site.Aliases,
            ToBackendKind(site.Backend),
            site.PhpVersion,
            site.ProxyUpstream,
            hasCertificate);
    }

    /// <summary>Maps the Sdk's cross-module backend value onto the agent client's.</summary>
    /// <param name="backend">The backend the owning module reported.</param>
    /// <returns>The agent client's matching kind.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown for a value the mapping does not know. This is a bug, not a domain failure: both enums
    /// are closed, so an unmapped value means the two were changed apart. Failing loudly beats
    /// defaulting to <see cref="SiteBackendKind.Static"/>, which would replace a PHP site's vhost
    /// with a static one at the very moment TLS was switched on.
    /// </exception>
    private static SiteBackendKind ToBackendKind(SiteBackend backend)
    {
        return backend switch
        {
            SiteBackend.Static => SiteBackendKind.Static,
            SiteBackend.Php => SiteBackendKind.Php,
            SiteBackend.ReverseProxy => SiteBackendKind.ReverseProxy,
            _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, "Unmapped site backend."),
        };
    }
}
