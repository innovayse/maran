using System.Text.Json.Serialization;
using Maran.SharedKernel.Pagination;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Files.Queries.SearchFileEntries;

/// <summary>One server page of the entries below a folder whose name contains a term, at any depth.</summary>
/// <remarks>
/// A search of the folder's own entries only is <c>ListFileEntriesQuery</c> with a search term. This
/// one descends, within the agent's bounds, and answers with the full path of every match.
/// </remarks>
/// <param name="AccountId">
/// The account whose home it is, resolved in the handler. The resolution is the tenant boundary:
/// another tenant's account answers "not found".
/// </param>
/// <param name="Path">The folder to search from, relative to the home; empty for the home itself.</param>
/// <param name="ShowHidden">Whether entries whose name starts with a dot are searched and returned.</param>
/// <param name="Page">The 1-based page.</param>
/// <param name="PageSize">Rows per page, one of the allowed sizes.</param>
/// <param name="Search">The text an entry's name must contain, ignoring case. Required.</param>
/// <param name="Sort">The sort key, or the default (by name, ascending).</param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and stamped by
/// the action. Never bound from the request; a refused read is journalled under it.</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request headers and
/// stamped by the action. Never bound from the request.</param>
public sealed record SearchFileEntriesQuery(
    Guid AccountId,
    string Path = "",
    bool ShowHidden = false,
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "") : IPagedQuery;
