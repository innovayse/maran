using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Pagination;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.SharedKernel.Extensions;

/// <summary>
/// The one implementation of paging over a database query: count the matching rows, order them by
/// the requested key then the tie-breaker (<see cref="SortMap{T}.Order(IQueryable{T}, SortRequest)"/>),
/// and read one page of them.
/// </summary>
/// <remarks>
/// <para>
/// The caller applies its search and typed filters to <c>source</c> first, and this counts what
/// remains — so the total is the total for the request, not for the table. Tenant scoping needs
/// nothing here: the module's global query filter is part of <c>source</c> before the count runs,
/// so a customer's total never counts another account's rows.
/// </para>
/// <para>
/// A page past the end is answered, not adjusted: empty rows, the true total, and the page that was
/// asked for. Clamping would give <c>Page</c> two meanings, and the caller already knows the total.
/// </para>
/// </remarks>
public static class QueryablePagingExtensions
{
    /// <summary>Orders, counts and pages a filtered database query.</summary>
    /// <typeparam name="T">The row type the query yields.</typeparam>
    /// <param name="source">The query, with the request's search and filters already applied.</param>
    /// <param name="query">The validated list request.</param>
    /// <param name="sortMap">The list's sort whitelist and tie-breaker.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The requested page with the total matching rows.</returns>
    public static async Task<PagedResult<T>> ToPagedAsync<T>(
        this IQueryable<T> source,
        IPagedQuery query,
        SortMap<T> sortMap,
        CancellationToken ct)
    {
        var offset = PageWindow.OffsetOf(query);

        // Composed before the count, which runs no query: an unvalidated sort throws here, whatever
        // the table holds, rather than only when a page has rows to order.
        var ordered = sortMap.Order(source, query.Sort);

        var total = await source.CountAsync(ct);
        if (offset >= total)
        {
            return new PagedResult<T>([], total, query.Page, query.PageSize);
        }

        // offset < total <= int.MaxValue, so the narrowing cannot lose a value.
        var items = await ordered
            .Skip((int)offset)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PagedResult<T>(items, total, query.Page, query.PageSize);
    }
}
