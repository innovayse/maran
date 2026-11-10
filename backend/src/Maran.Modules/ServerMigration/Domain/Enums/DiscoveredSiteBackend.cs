namespace Maran.Modules.ServerMigration.Domain.Enums;

/// <summary>How a discovered site's web server serves it.</summary>
public enum DiscoveredSiteBackend
{
    /// <summary>The configuration did not say, or said something this reader does not understand.</summary>
    Unknown,

    /// <summary>Static files only.</summary>
    Static,

    /// <summary>Handed to PHP.</summary>
    Php,

    /// <summary>Proxied to another process.</summary>
    Proxy,
}
