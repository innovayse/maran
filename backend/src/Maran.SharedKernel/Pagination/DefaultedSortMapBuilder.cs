using System.Collections.Frozen;
using System.Linq.Expressions;

namespace Maran.SharedKernel.Pagination;

/// <summary>
/// The last step of building a <see cref="SortMap{T}"/>: the fields and the default are fixed, and
/// only the tie-breaker is missing. A separate type so a map without a tie-breaker cannot be built —
/// the compiler, not a run-time check, refuses it.
/// </summary>
/// <typeparam name="T">The row type being ordered.</typeparam>
public sealed class DefaultedSortMapBuilder<T>
{
    /// <summary>The registered fields, by public name.</summary>
    private readonly FrozenDictionary<string, SortField<T>> _fields;

    /// <summary>The order applied when the caller names none.</summary>
    private readonly SortKey _default;

    /// <summary>Captures what <see cref="SortMapBuilder{T}.DefaultTo"/> has already checked.</summary>
    /// <param name="fields">The registered fields.</param>
    /// <param name="defaultKey">A key naming one of <paramref name="fields"/>.</param>
    internal DefaultedSortMapBuilder(FrozenDictionary<string, SortField<T>> fields, SortKey defaultKey)
    {
        _fields = fields;
        _default = defaultKey;
    }

    /// <summary>
    /// Names the member, unique per row, that breaks every tie — normally the id — and builds the map.
    /// In memory two keys compare with <see cref="Comparer{T}.Default"/>.
    /// </summary>
    /// <typeparam name="TKey">The member's type.</typeparam>
    /// <param name="member">The unique member.</param>
    /// <returns>The finished map.</returns>
    public SortMap<T> TieBreaker<TKey>(Expression<Func<T, TKey>> member)
    {
        return new SortMap<T>(_fields, _default, new TypedSortField<T, TKey>(member, Comparer<TKey>.Default));
    }

    /// <summary>
    /// Names a text member, unique per row, that breaks every tie, and builds the map. In memory two
    /// values compare ordinally and case-sensitively — the comparison under which they are unique.
    /// </summary>
    /// <param name="member">The unique member.</param>
    /// <returns>The finished map.</returns>
    public SortMap<T> TieBreaker(Expression<Func<T, string>> member)
    {
        return new SortMap<T>(_fields, _default, new TypedSortField<T, string>(member, StringComparer.Ordinal));
    }
}
