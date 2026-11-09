using System.Text.Json.Serialization;
using Maran.SharedKernel.Pagination;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Files.Queries.ListTrashEntries;

/// <summary>One server page of the entries in an account's trash.</summary>
/// <remarks>
/// The agent answers the whole trash at once, bounded at its own ceiling, so this module searches, orders
/// and pages the answer. A search matches an entry's name or the path it was trashed from.
/// </remarks>
/// <param name="AccountId">
/// The account whose trash it is, resolved in the handler. The resolution is the tenant boundary:
/// another tenant's account answers "not found".
/// </param>
/// <param name="Page">The 1-based page.</param>
/// <param name="PageSize">Rows per page, one of the allowed sizes.</param>
/// <param name="Search">Text the entry's name or original path must contain, ignoring case; none for every entry.</param>
/// <param name="Sort">The sort key, or the default (newest first).</param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and stamped by
/// the action. Never bound from the request; recorded when the account is refused.</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request headers and
/// stamped by the action. Never bound from the request; recorded when the account is refused.</param>
public sealed record ListTrashEntriesQuery(
    Guid AccountId,
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "") : IPagedQuery;
