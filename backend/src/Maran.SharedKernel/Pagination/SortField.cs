using Maran.SharedKernel.Enums;

namespace Maran.SharedKernel.Pagination;

/// <summary>
/// One member a <see cref="SortMap{T}"/> can order by, with its key type erased so a map can hold
/// fields of different key types side by side. The only implementation is
/// <see cref="TypedSortField{T, TKey}"/>, which keeps the key type all the way to
/// <c>OrderBy&lt;T, TKey&gt;</c>: nothing is ever boxed to <see cref="object"/>, so EF Core translates
/// a <see cref="DateTimeOffset"/>, an enum or a <see cref="Guid"/> column as itself.
/// </summary>
/// <typeparam name="T">The row type being ordered.</typeparam>
internal abstract class SortField<T>
{
    /// <summary>Starts a database order by this member.</summary>
    /// <param name="source">The query to order.</param>
    /// <param name="direction">The direction.</param>
    /// <returns>The ordered query.</returns>
    public abstract IOrderedQueryable<T> OrderBy(IQueryable<T> source, SortDirection direction);

    /// <summary>Adds this member as a subsequent database order.</summary>
    /// <param name="source">The already ordered query.</param>
    /// <param name="direction">The direction.</param>
    /// <returns>The ordered query.</returns>
    public abstract IOrderedQueryable<T> ThenBy(IOrderedQueryable<T> source, SortDirection direction);

    /// <summary>Starts an in-memory order by this member.</summary>
    /// <param name="source">The rows to order.</param>
    /// <param name="direction">The direction.</param>
    /// <returns>The ordered rows.</returns>
    public abstract IOrderedEnumerable<T> OrderBy(IEnumerable<T> source, SortDirection direction);

    /// <summary>Adds this member as a subsequent in-memory order.</summary>
    /// <param name="source">The already ordered rows.</param>
    /// <param name="direction">The direction.</param>
    /// <returns>The ordered rows.</returns>
    public abstract IOrderedEnumerable<T> ThenBy(IOrderedEnumerable<T> source, SortDirection direction);
}
