namespace Maran.Agent.Client.Services.SitesService;

/// <summary>Which web server owns ports 80 and 443 for a site, as the panel names it.</summary>
/// <remarks>
/// A panel-side mirror of the wire's <c>SiteWebServer</c> so callers outside this project never hold a
/// generated protobuf type. It deliberately has no "unspecified" member: the wire reads that as nginx, so a caller
/// that forgot to choose would silently render Apache sites for nginx; it must not be able to express it.
/// </remarks>
public enum SiteWebServer
{
    /// <summary>nginx, the same daemon that fronts the panel.</summary>
    Nginx = 1,

    /// <summary>Apache, reaching PHP through <c>proxy_fcgi</c>.</summary>
    Apache = 2,
}
