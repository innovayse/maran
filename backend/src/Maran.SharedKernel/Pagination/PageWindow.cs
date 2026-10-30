using Maran.SharedKernel.Interfaces;

namespace Maran.SharedKernel.Pagination;

/// <summary>
/// The arithmetic both pagers share: where a requested page starts, and whether it starts past the
/// last row. Kept here so the database pager and the in-memory pager cannot compute a page two ways.
/// </summary>
public static class PageWindow
{
    /// <summary>
    /// The number of rows before the requested page. Computed in 64 bits: a validated page has no
    /// upper bound, and <c>(Page - 1) * PageSize</c> overflows 32 bits long before it is refused.
    /// </summary>
    /// <param name="query">A request whose page and size the list's validator has accepted.</param>
    /// <returns>The offset of the page's first row.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The page is below the first page, or the size is not an allowed size — the list's validator did not run.
    /// </exception>
    public static long OffsetOf(IPagedQuery query)
    {
        if (query.Page < PageDefaults.FirstPage)
        {
            throw new ArgumentOutOfRangeException(nameof(query), query.Page, "The page is below the first page.");
        }

        if (!PageDefaults.AllowedSizes.Contains(query.PageSize))
        {
            throw new ArgumentOutOfRangeException(
                nameof(query),
                query.PageSize,
                "The page size is not an allowed size.");
        }

        return (long)(query.Page - PageDefaults.FirstPage) * query.PageSize;
    }
}
