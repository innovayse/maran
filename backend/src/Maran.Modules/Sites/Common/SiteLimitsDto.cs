namespace Maran.Modules.Sites.Common;

/// <summary>
/// The bounds a site's form must respect, read from the server rather than written into the SPA
/// (rules/architecture.md "The backend owns the data, the SPA renders it").
/// </summary>
/// <param name="MaximumAliases">The most aliases one site may carry.</param>
public sealed record SiteLimitsDto(int MaximumAliases);
