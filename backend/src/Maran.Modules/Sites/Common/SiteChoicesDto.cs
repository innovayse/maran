namespace Maran.Modules.Sites.Common;

/// <summary>
/// The closed sets the sites screens select from, read from the server rather than written into the
/// SPA (rules/architecture.md "The backend owns the data, the SPA renders it"). The server keeps
/// validating every submitted value against the same enums these lists are built from.
/// </summary>
/// <param name="BackendTypes">What can serve a site, in the order the form offers them.</param>
/// <param name="Statuses">Whether a site serves its own content, in the order the filter offers them.</param>
public sealed record SiteChoicesDto(IReadOnlyList<SiteChoiceDto> BackendTypes, IReadOnlyList<SiteChoiceDto> Statuses);
