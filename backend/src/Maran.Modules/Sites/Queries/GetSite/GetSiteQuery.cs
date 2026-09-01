namespace Maran.Modules.Sites.Queries.GetSite;

/// <summary>Reads one site.</summary>
/// <param name="SiteId">The site to read; another tenant's id answers "not found".</param>
public sealed record GetSiteQuery(Guid SiteId);
