using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Dns.Queries.ListZoneTemplates;

/// <summary>
/// Reads one page of the zone templates, optionally searched. Bound straight from the query string
/// (<c>GET /api/v1/dns-zone-templates?page=2&amp;search=web&amp;sort=-updatedAt</c>);
/// <see cref="ListZoneTemplatesQueryValidator"/> holds every member to its bounds before the handler runs.
/// Administrator only.
/// </summary>
/// <param name="Page">The 1-based page requested.</param>
/// <param name="PageSize">Rows per page; one of <see cref="PageDefaults.AllowedSizes"/>.</param>
/// <param name="Search">A term matched literally inside the template's name, or <see cref="SearchTerm.None"/>.</param>
/// <param name="Sort">One of <see cref="ZoneTemplateSortFields"/> either way, or <see cref="SortRequest.Default"/>.</param>
public sealed record ListZoneTemplatesQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default) : IPagedQuery;
