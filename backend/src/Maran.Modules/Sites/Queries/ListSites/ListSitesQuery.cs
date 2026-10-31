using Maran.Modules.Sites.Domain.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Sites.Queries.ListSites;

/// <summary>
/// Reads one page of the sites the caller may see, optionally searched and narrowed to an account, a
/// backend, a PHP version or a status. Bound straight from the query string
/// (<c>GET /api/v1/sites?page=2&amp;search=shop&amp;status=disabled&amp;backendType=reverseProxy</c>);
/// <see cref="ListSitesQueryValidator"/> holds every member to its bounds before the handler runs.
/// </summary>
/// <remarks>
/// <para>
/// The enum filters are written on the query string in the camelCase the JSON answers carry
/// (<c>disabled</c>, <c>reverseProxy</c>), so the SPA sends back exactly the spelling it reads. The
/// binder parses an enum name ignoring case, so that spelling binds; a number naming no member is
/// refused while binding, with <c>RequestParameterInvalid</c>.
/// </para>
/// <para>
/// The tenant scope never comes from this query: the context's global query filter scopes the read to
/// the caller's own account (rules/security.md item 6). <paramref name="AccountId"/> only narrows that
/// scope further, and the two are combined with AND — a customer naming another account's id gets an
/// empty page with a total of zero, never that account's rows.
/// </para>
/// </remarks>
/// <param name="Page">The 1-based page requested; a page past the end answers empty with the true total.</param>
/// <param name="PageSize">Rows per page; one of <see cref="PageDefaults.AllowedSizes"/>.</param>
/// <param name="Search">
/// A term matched literally inside the domain or the owning account's name, or <see cref="SearchTerm.None"/>.
/// </param>
/// <param name="Sort">
/// One of <see cref="SiteSortFields"/> either way (<c>domain</c>, <c>-createdAt</c>, …), or
/// <see cref="SortRequest.Default"/> for oldest first.
/// </param>
/// <param name="AccountId">Only this account's sites, or <c>null</c> for every account the caller may see.</param>
/// <param name="BackendType">Only sites served by this backend, or <c>null</c> for every backend.</param>
/// <param name="PhpVersion">
/// Only sites bound to this PHP version (<c>8.3</c>), or <see cref="FilterValue.None"/> for every
/// version.
/// </param>
/// <param name="Status">Only sites in this state, or <c>null</c> for every state.</param>
public sealed record ListSitesQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default,
    Guid? AccountId = null,
    SiteBackendType? BackendType = null,
    FilterValue PhpVersion = default,
    SiteStatus? Status = null) : IPagedQuery;
