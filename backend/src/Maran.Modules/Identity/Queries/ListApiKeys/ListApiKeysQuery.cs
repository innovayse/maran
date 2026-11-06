using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Identity.Queries.ListApiKeys;

/// <summary>
/// Reads one page of the panel's API keys, optionally searched by name and narrowed to one status.
/// Bound straight from the query string
/// (<c>GET /api/v1/api-keys?page=2&amp;search=ci&amp;status=active&amp;sort=-lastUsedAt</c>);
/// <see cref="ListApiKeysQueryValidator"/> holds every member to its bounds before the handler runs.
/// </summary>
/// <remarks>
/// Keys belong to the panel, not to the administrator who created them, so every administrator sees the
/// same list; the controller's policy decides who may read it and nothing here narrows by creator.
/// </remarks>
/// <param name="Page">The 1-based page requested; a page past the end answers empty with the true total.</param>
/// <param name="PageSize">Rows per page; one of <see cref="PageDefaults.AllowedSizes"/>.</param>
/// <param name="Search">A term matched literally inside the key's name, or <see cref="SearchTerm.None"/>.</param>
/// <param name="Sort">
/// One of <see cref="ApiKeySortFields"/> either way (<c>name</c>, <c>-lastUsedAt</c>), or
/// <see cref="SortRequest.Default"/> for newest created first.
/// </param>
/// <param name="Status">
/// One of <see cref="ApiKeyStatusFilters"/> to list only keys in that state, or
/// <see cref="FilterValue.None"/> for every key.
/// </param>
public sealed record ListApiKeysQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default,
    FilterValue Status = default) : IPagedQuery;
