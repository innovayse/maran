using Maran.Agent.Client.Services.SitesService;
using Maran.Modules.Sites.Domain;
using Maran.Modules.Sites.Domain.Enums;

namespace Maran.Modules.Sites.Common;

/// <summary>
/// Builds the <see cref="SiteDescriptor"/> the agent requires on every rpc that re-renders a
/// vhost, from the stored <see cref="Site"/> that defines the site.
/// </summary>
/// <remarks>
/// It exists so that no handler ever assembles a descriptor by hand. Every field is read from the
/// row; none is a literal at a call site. That is the whole point:
/// <see cref="SiteDescriptor.HasCertificate"/> passed as a literal <c>false</c> would re-render a
/// live site's vhost without its TLS block and drop it to plain HTTP, and the mistake would look
/// perfectly reasonable in a diff. With one conversion, there is one place to be right.
/// </remarks>
public static class SiteDescriptorFactory
{
    /// <summary>Describes a stored site to the agent.</summary>
    /// <param name="site">The row that defines the site.</param>
    /// <returns>The descriptor carrying that row's own facts.</returns>
    public static SiteDescriptor From(Site site)
    {
        return new SiteDescriptor(
            site.Aliases,
            ToBackendKind(site.BackendType),
            site.PhpVersion,
            site.ProxyUpstream,
            site.HasCertificate);
    }

    /// <summary>Maps the module's backend enum onto the agent client's.</summary>
    /// <param name="backendType">The stored backend type.</param>
    /// <returns>The agent client's matching kind.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown for a value the mapping does not know. This is a bug, not a domain failure: the enum
    /// is closed and the database stores its names, so an unmapped value means the two were changed
    /// apart. Failing loudly beats defaulting to <c>Static</c>, which would silently replace a PHP
    /// site's vhost with a static one.
    /// </exception>
    private static SiteBackendKind ToBackendKind(SiteBackendType backendType)
    {
        return backendType switch
        {
            SiteBackendType.Static => SiteBackendKind.Static,
            SiteBackendType.Php => SiteBackendKind.Php,
            SiteBackendType.ReverseProxy => SiteBackendKind.ReverseProxy,
            _ => throw new ArgumentOutOfRangeException(nameof(backendType), backendType, "Unmapped site backend type."),
        };
    }
}
