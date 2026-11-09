using System.Text.Json.Serialization;
using Maran.SharedKernel.Pagination;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Files.Queries.ListFileEntries;

/// <summary>One server page of the entries directly inside a folder of an account's home.</summary>
/// <remarks>
/// With no <paramref name="Search"/> the agent pages the folder itself, so a folder of a hundred
/// thousand names costs one page of work. With a search the agent finds the entries of this folder
/// whose name contains the term (at most the agent's result ceiling) and this module pages those.
/// A search through the subfolders is <c>SearchFileEntriesQuery</c>.
/// </remarks>
/// <param name="AccountId">
/// The account whose home it is, resolved in the handler. The resolution is the tenant boundary:
/// another tenant's account answers "not found".
/// </param>
/// <param name="Path">The folder, relative to the home; empty for the home itself.</param>
/// <param name="ShowHidden">Whether entries whose name starts with a dot are listed.</param>
/// <param name="Page">The 1-based page.</param>
/// <param name="PageSize">Rows per page, one of the allowed sizes.</param>
/// <param name="Search">Text the entry's name must contain, ignoring case; none for every entry.</param>
/// <param name="Sort">The sort key, or the default (by name, ascending).</param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and stamped by
/// the action. Never bound from the request; a refused read is journalled under it.</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request headers and
/// stamped by the action. Never bound from the request.</param>
public sealed record ListFileEntriesQuery(
    Guid AccountId,
    string Path = "",
    bool ShowHidden = false,
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "") : IPagedQuery;
