namespace Maran.Modules.Sites.Queries.GetSiteOptions;

/// <summary>Reads one site's per-site options, with the bounds and choices their screen needs.</summary>
/// <param name="SiteId">The site whose options to read; another tenant's id answers "not found".</param>
public sealed record GetSiteOptionsQuery(Guid SiteId);
