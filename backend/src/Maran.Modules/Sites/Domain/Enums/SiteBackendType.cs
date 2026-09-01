namespace Maran.Modules.Sites.Domain.Enums;

/// <summary>What serves a site's content.</summary>
/// <remarks>
/// A closed value set, stored as its own name rather than an ordinal so a reader of the database
/// sees the same word the code does. It deliberately has no "unspecified" member: the agent refuses
/// that value, and a site whose backend the panel cannot name is a site whose vhost cannot be
/// rendered.
/// </remarks>
public enum SiteBackendType
{
    /// <summary>Static files only, with no application server.</summary>
    Static = 1,

    /// <summary>A PHP-FPM pool bound to a specific installed version.</summary>
    Php = 2,

    /// <summary>Reverse-proxied to an upstream the customer names explicitly.</summary>
    ReverseProxy = 3,
}
