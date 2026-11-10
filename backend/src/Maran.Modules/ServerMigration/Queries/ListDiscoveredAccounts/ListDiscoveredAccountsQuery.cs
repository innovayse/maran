using System.Text.Json.Serialization;
using Maran.SharedKernel.Pagination;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.ServerMigration.Queries.ListDiscoveredAccounts;

/// <summary>
/// Reads one page of the accounts a discovery found on a remote server, optionally searched by login or domain and
/// narrowed to those that have sites, databases, scheduled jobs or mailboxes. Bound straight from the query string
/// (<c>GET /api/v1/remote-servers/{id}/accounts?search=shop&amp;has=databases&amp;sort=-disk</c>);
/// <see cref="ListDiscoveredAccountsQueryValidator"/> holds every member to its bounds.
/// </summary>
/// <param name="Page">The 1-based page requested; a page past the end answers empty with the true total.</param>
/// <param name="PageSize">Rows per page; one of <see cref="PageDefaults.AllowedSizes"/>.</param>
/// <param name="Search">A term matched literally inside the login or the domains, or <see cref="SearchTerm.None"/>.</param>
/// <param name="Sort">One of <see cref="DiscoveredAccountSortFields"/> either way, or <see cref="SortRequest.Default"/>.</param>
/// <param name="Has">
/// One of <see cref="DiscoveredAccountHasFilters"/>, or <see cref="FilterValue.None"/> for every account. A spelling
/// that names none of them matches no account.
/// </param>
/// <param name="ServerId">
/// The server, from the route. Established by the action and never bound from the query string or a body.
/// </param>
public sealed record ListDiscoveredAccountsQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default,
    FilterValue Has = default,
    [property: JsonIgnore][property: BindNever][BindNever] Guid ServerId = default) : IPagedQuery;
