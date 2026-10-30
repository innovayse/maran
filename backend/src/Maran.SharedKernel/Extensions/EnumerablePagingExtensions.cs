using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Pagination;
using Maran.SharedKernel.Results;

namespace Maran.SharedKernel.Extensions;

/// <summary>
/// The same paging contract as <see cref="QueryablePagingExtensions"/>, over a list held in memory —
/// the bounded answer the agent returns whole (cron entries, firewall rules, PHP versions). Same
/// <see cref="SortMap{T}"/>, same <see cref="PageWindow"/>, same page-past-the-end answer.
/// </summary>
public static class EnumerablePagingExtensions
{
    /// <summary>
    /// Searches, orders and pages a list held in memory. With a search term, a row matches when any
    /// of <paramref name="searchFields"/> contains the term, compared ordinally and ignoring case;
    /// with no search fields a term matches nothing. <see cref="SearchTerm.None"/> matches every row.
    /// </summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="source">Every row of the list.</param>
    /// <param name="query">The validated list request.</param>
    /// <param name="sortMap">The list's sort whitelist and tie-breaker.</param>
    /// <param name="searchFields">The text members a search looks in.</param>
    /// <returns>The requested page with the total matching rows.</returns>
    public static PagedResult<T> ToPaged<T>(
        this IEnumerable<T> source,
        IPagedQuery query,
        SortMap<T> sortMap,
        IReadOnlyList<Func<T, string>> searchFields)
    {
        var offset = PageWindow.OffsetOf(query);

        var matching = query.Search.IsNone
            ? source.ToList()
            : source.Where(row => { return Matches(row, query.Search.Value, searchFields); }).ToList();

        // Composed before the page-past-the-end answer and evaluated lazily: an unvalidated sort
        // throws here, whatever the list holds.
        var ordered = sortMap.Order(matching, query.Sort);

        var total = matching.Count;
        if (offset >= total)
        {
            return new PagedResult<T>([], total, query.Page, query.PageSize);
        }

        // offset < total, so the narrowing cannot lose a value.
        var items = ordered
            .Skip((int)offset)
            .Take(query.PageSize)
            .ToList();

        return new PagedResult<T>(items, total, query.Page, query.PageSize);
    }

    /// <summary>
    /// Whether any searched member of <paramref name="row"/> contains <paramref name="term"/>, ignoring case.
    /// </summary>
    /// <param name="row">The candidate row.</param>
    /// <param name="term">The non-empty search term.</param>
    /// <param name="searchFields">The text members to look in.</param>
    /// <returns>True when the row matches.</returns>
    private static bool Matches<T>(T row, string term, IReadOnlyList<Func<T, string>> searchFields)
    {
        return searchFields.Any(field => { return field(row).Contains(term, StringComparison.OrdinalIgnoreCase); });
    }
}
