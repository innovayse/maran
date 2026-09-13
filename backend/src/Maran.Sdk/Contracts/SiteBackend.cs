namespace Maran.Sdk.Contracts;

/// <summary>Which backend serves a site's content, as one module reports it to another.</summary>
/// <remarks>
/// A deliberate second declaration of a closed set the Sites module already owns, and not a leak of
/// it: the Sdk may not reference a module, and a module may not reference another module, so a
/// cross-module snapshot cannot carry the owning module's own enum. The values are named, not
/// numbered by position — a reader mapping this onto the agent's kinds maps by name.
/// </remarks>
public enum SiteBackend
{
    /// <summary>Static files only, no application server.</summary>
    Static = 1,

    /// <summary>A PHP-FPM pool bound to the site's PHP version.</summary>
    Php = 2,

    /// <summary>Reverse-proxied to an upstream the site names explicitly.</summary>
    ReverseProxy = 3,
}
