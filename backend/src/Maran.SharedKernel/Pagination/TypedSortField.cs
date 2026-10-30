using System.Linq.Expressions;
using Maran.SharedKernel.Enums;

namespace Maran.SharedKernel.Pagination;

/// <summary>
/// A <see cref="SortField{T}"/> over a member of key type <typeparamref name="TKey"/>: the expression
/// for a database query, its compiled form and a comparer of the same key type for a list in memory.
/// </summary>
/// <typeparam name="T">The row type being ordered.</typeparam>
/// <typeparam name="TKey">The member's own type — never <see cref="object"/>.</typeparam>
internal sealed class TypedSortField<T, TKey> : SortField<T>
{
    /// <summary>The member, as an expression a query provider translates.</summary>
    private readonly Expression<Func<T, TKey>> _member;

    /// <summary>The member, compiled for rows held in memory.</summary>
    private readonly Func<T, TKey> _compiled;

    /// <summary>How two keys compare in memory.</summary>
    private readonly IComparer<TKey> _comparer;

    /// <summary>Creates the field.</summary>
    /// <param name="member">The member to order by.</param>
    /// <param name="comparer">How two keys compare when the rows are held in memory.</param>
    public TypedSortField(Expression<Func<T, TKey>> member, IComparer<TKey> comparer)
    {
        _member = member;
        _compiled = member.Compile();
        _comparer = comparer;
    }

    /// <inheritdoc />
    public override IOrderedQueryable<T> OrderBy(IQueryable<T> source, SortDirection direction)
    {
        return direction == SortDirection.Descending
            ? source.OrderByDescending(_member)
            : source.OrderBy(_member);
    }

    /// <inheritdoc />
    public override IOrderedQueryable<T> ThenBy(IOrderedQueryable<T> source, SortDirection direction)
    {
        return direction == SortDirection.Descending
            ? source.ThenByDescending(_member)
            : source.ThenBy(_member);
    }

    /// <inheritdoc />
    public override IOrderedEnumerable<T> OrderBy(IEnumerable<T> source, SortDirection direction)
    {
        return direction == SortDirection.Descending
            ? source.OrderByDescending(_compiled, _comparer)
            : source.OrderBy(_compiled, _comparer);
    }

    /// <inheritdoc />
    public override IOrderedEnumerable<T> ThenBy(IOrderedEnumerable<T> source, SortDirection direction)
    {
        return direction == SortDirection.Descending
            ? source.ThenByDescending(_compiled, _comparer)
            : source.ThenBy(_compiled, _comparer);
    }
}
