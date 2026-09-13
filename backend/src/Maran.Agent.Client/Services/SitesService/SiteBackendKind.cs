namespace Maran.Agent.Client.Services.SitesService;

/// <summary>What serves a site's content, as the panel names it.</summary>
/// <remarks>
/// A panel-side mirror of the wire's <c>SiteBackendType</c> so callers outside this project never
/// hold a generated protobuf type. It deliberately has no "unspecified" member: the agent refuses
/// that value, so a caller must not be able to express it.
/// </remarks>
public enum SiteBackendKind
{
    /// <summary>Static files only, with no application server.</summary>
    Static = 1,

    /// <summary>A PHP-FPM pool bound to a specific installed version.</summary>
    Php = 2,

    /// <summary>Reverse-proxied to an upstream the caller names explicitly.</summary>
    ReverseProxy = 3,
}
