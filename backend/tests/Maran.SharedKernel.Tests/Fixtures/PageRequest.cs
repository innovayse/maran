using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Pagination;

namespace Maran.SharedKernel.Tests.Fixtures;

/// <summary>
/// A list request shaped the way every module's list query is: positional, with the contract's defaults.
/// </summary>
/// <param name="Page">The 1-based page requested.</param>
/// <param name="PageSize">Rows per page.</param>
/// <param name="Search">Search term, or <see cref="SearchTerm.None"/>.</param>
/// <param name="Sort">Sort key, or <see cref="SortRequest.Default"/> for the default order.</param>
public sealed record PageRequest(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default) : IPagedQuery;
