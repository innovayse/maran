namespace Maran.Agent.Client.Services.SitesService;

/// <summary>Which of a site's two logs is being tailed.</summary>
public enum SiteLogSource
{
    /// <summary>The access log.</summary>
    Access = 1,

    /// <summary>The error log.</summary>
    Error = 2,
}
