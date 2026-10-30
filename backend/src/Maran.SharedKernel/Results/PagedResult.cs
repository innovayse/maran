namespace Maran.SharedKernel.Results;

/// <summary>
/// One page of a list: the rows on it, the total number of rows matching the request across every
/// page, and the page and size that were asked for — echoed, never adjusted.
/// </summary>
/// <typeparam name="T">The row type.</typeparam>
/// <param name="Items">The rows on this page; empty for a page past the end.</param>
/// <param name="Total">Rows matching the request's search and filters across all pages.</param>
/// <param name="Page">The 1-based page the caller asked for.</param>
/// <param name="PageSize">The page size the caller asked for.</param>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    /// <summary>Translates every row on the page, keeping the total and the paging untouched.</summary>
    /// <typeparam name="TOut">The translated row type, normally a DTO.</typeparam>
    /// <param name="selector">The translation of one row.</param>
    /// <returns>The same page over the translated rows.</returns>
    public PagedResult<TOut> Map<TOut>(Func<T, TOut> selector)
    {
        return new PagedResult<TOut>(Items.Select(selector).ToList(), Total, Page, PageSize);
    }
}
