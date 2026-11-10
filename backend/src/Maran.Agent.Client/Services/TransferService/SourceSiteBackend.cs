namespace Maran.Agent.Client.Services.TransferService;

/// <summary>What serves a site found on a source.</summary>
public enum SourceSiteBackend
{
    /// <summary>
    /// The agent could not tell. A site is listed all the same, so this is a value and not an absence:
    /// the site exists, only its serving mode is unknown.
    /// </summary>
    Unknown = 0,

    /// <summary>Static files.</summary>
    Static = 1,

    /// <summary>PHP.</summary>
    Php = 2,

    /// <summary>A reverse proxy to an upstream.</summary>
    Proxy = 3,
}
