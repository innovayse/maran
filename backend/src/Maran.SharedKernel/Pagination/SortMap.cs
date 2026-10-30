using System.Collections.Frozen;

namespace Maran.SharedKernel.Pagination;

/// <summary>
/// The whitelist of orders one list accepts: each public sort name bound to a typed member, the
/// order used when the caller names none, and the tie-breaker that makes every order total. Built
/// with <see cref="SortMapBuilder{T}"/>.
/// </summary>
/// <typeparam name="T">
/// The row type being ordered — an entity for a database list, a DTO for an agent list.
/// </typeparam>
/// <remarks>
/// <para>
/// <b>The keys are the whitelist, and nothing else is.</b> The list's validator accepts a sort
/// exactly when <see cref="Accepts"/> does, and the pagers order by exactly what <see cref="Order(IQueryable{T}, SortRequest)"/>
/// applies, both reading this one map — so the accepted names are written once and a caller can
/// never order by a column the list did not choose to expose.
/// </para>
/// <para>
/// <b>Every field keeps its own key type.</b> A member is stored with its type, never boxed to
/// <see cref="object"/>, so EF Core orders a <see cref="DateTimeOffset"/>, an enum or a
/// <see cref="Guid"/> column as itself rather than through a <c>Convert(object)</c>, and an in-memory
/// list compares two keys with that type's own comparer.
/// </para>
/// <para>
/// <b>The tie-breaker is mandatory, and it follows the direction of the chosen order.</b> Offset
/// paging over an order with ties is not stable: two rows sharing a <c>CreatedAt</c> can trade
/// places between two page requests, so one row appears on both pages and its neighbour on neither.
/// Every order is therefore followed by the tie-breaker, which must be unique per row (the id).
/// For stability alone either direction would do — any fixed total order pages stably. The
/// direction is decided by what a reader expects of <c>-x</c>: that it is <c>x</c> read backwards.
/// With a tie-breaker fixed ascending it is not — within each run of ties the rows keep their
/// ascending order — so the last page of <c>createdAt</c> is not the first page of
/// <c>-createdAt</c> reversed. Flipping the tie-breaker with the order makes the descending
/// sequence exactly the ascending one reversed, which is the one definition of "descending" that
/// needs no footnote.
/// </para>
/// </remarks>
public sealed class SortMap<T>
{
    /// <summary>Public sort name to the typed member it orders by, compared ordinally.</summary>
    private readonly FrozenDictionary<string, SortField<T>> _fields;

    /// <summary>The member, unique per row, that breaks every tie.</summary>
    private readonly SortField<T> _tieBreaker;

    /// <summary>The order applied when the caller names none.</summary>
    public SortKey Default { get; }

    /// <summary>The public sort names this list accepts.</summary>
    public IReadOnlyCollection<string> Fields
    {
        get { return _fields.Keys; }
    }

    /// <summary>Creates the map from what the builder has already checked.</summary>
    /// <param name="fields">The registered fields, every name well formed.</param>
    /// <param name="defaultKey">A key naming one of <paramref name="fields"/>.</param>
    /// <param name="tieBreaker">The member unique per row.</param>
    internal SortMap(FrozenDictionary<string, SortField<T>> fields, SortKey defaultKey, SortField<T> tieBreaker)
    {
        _fields = fields;
        Default = defaultKey;
        _tieBreaker = tieBreaker;
    }

    /// <summary>Whether a list request's sort is one this list accepts: the default, or a key naming a field of the map.</summary>
    /// <param name="request">The request's sort.</param>
    /// <returns>True when <see cref="Resolve"/> will answer for <paramref name="request"/>.</returns>
    public bool Accepts(SortRequest request)
    {
        return request.IsDefault || _fields.ContainsKey(request.Key.Field);
    }

    /// <summary>Resolves a request's sort into the key to order by: <see cref="Default"/> for the default.</summary>
    /// <param name="request">The request's sort, already accepted by <see cref="Accepts"/>.</param>
    /// <returns>The key to order by.</returns>
    /// <exception cref="ArgumentException">The sort is not accepted — the list's validator did not run.</exception>
    public SortKey Resolve(SortRequest request)
    {
        if (request.IsDefault)
        {
            return Default;
        }

        if (!_fields.ContainsKey(request.Key.Field))
        {
            throw new ArgumentException($"'{request}' is not a sort this list accepts.", nameof(request));
        }

        return request.Key;
    }

    /// <summary>Orders a database query by the requested field, then the tie-breaker in the same direction.</summary>
    /// <param name="source">The query to order.</param>
    /// <param name="request">The request's sort, already accepted by <see cref="Accepts"/>.</param>
    /// <returns>The totally ordered query.</returns>
    /// <exception cref="ArgumentException">The sort is not accepted — the list's validator did not run.</exception>
    public IOrderedQueryable<T> Order(IQueryable<T> source, SortRequest request)
    {
        var key = Resolve(request);
        return _tieBreaker.ThenBy(_fields[key.Field].OrderBy(source, key.Direction), key.Direction);
    }

    /// <summary>Orders rows held in memory by the requested field, then the tie-breaker in the same direction.</summary>
    /// <param name="source">The rows to order.</param>
    /// <param name="request">The request's sort, already accepted by <see cref="Accepts"/>.</param>
    /// <returns>The totally ordered rows.</returns>
    /// <exception cref="ArgumentException">The sort is not accepted — the list's validator did not run.</exception>
    public IOrderedEnumerable<T> Order(IEnumerable<T> source, SortRequest request)
    {
        var key = Resolve(request);
        return _tieBreaker.ThenBy(_fields[key.Field].OrderBy(source, key.Direction), key.Direction);
    }
}
