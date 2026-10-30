using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Pagination;

namespace Maran.Sdk.Tests.Fixtures;

/// <summary>
/// A list query shaped the way every module's is: positional, with the paging contract's defaults.
/// </summary>
/// <param name="Page">The 1-based page requested.</param>
/// <param name="PageSize">Rows per page.</param>
/// <param name="Search">The search, or <see cref="SearchTerm.None"/>.</param>
/// <param name="Sort">The sort, or <see cref="SortRequest.Default"/>.</param>
public sealed record ListRequest(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default) : IPagedQuery;
