using System.Collections.Frozen;
using System.Linq.Expressions;
using Maran.SharedKernel.Enums;

namespace Maran.SharedKernel.Pagination;

/// <summary>
/// Builds a <see cref="SortMap{T}"/> one typed field at a time:
/// <c>new SortMapBuilder&lt;Site&gt;().By(…).By(…).DefaultTo(…).TieBreaker(…)</c>. Every mistake a map
/// can be built with — a malformed name, a name registered twice, a default naming no field — throws
/// at the call that makes it, so a broken map fails the first test that touches its list.
/// </summary>
/// <remarks>
/// A builder is an object created with <c>new</c> rather than a static <c>SortMap&lt;T&gt;.By</c>
/// entry point because a static member on a generic type is what CA1000 refuses, and the rule is
/// kept rather than switched off.
/// </remarks>
/// <typeparam name="T">The row type being ordered — an entity for a database list, a DTO for an agent list.</typeparam>
public sealed class SortMapBuilder<T>
{
    /// <summary>The fields registered so far, by public name, compared ordinally.</summary>
    private readonly Dictionary<string, SortField<T>> _fields = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a field ordered by a member of any comparable type — a date, an enum, a number, a
    /// <see cref="Guid"/>. In memory two keys compare with <see cref="Comparer{T}.Default"/>.
    /// </summary>
    /// <typeparam name="TKey">The member's type.</typeparam>
    /// <param name="field">The public sort name, such as <c>createdAt</c>.</param>
    /// <param name="member">The member it orders by.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">The name is malformed or already registered.</exception>
    public SortMapBuilder<T> By<TKey>(string field, Expression<Func<T, TKey>> member)
    {
        return Register(field, new TypedSortField<T, TKey>(member, Comparer<TKey>.Default));
    }

    /// <summary>
    /// Registers a field ordered by a text member. In memory two values compare ordinally ignoring
    /// case, so an agent list's order does not move with the server's culture; a database list
    /// orders by the column's collation.
    /// </summary>
    /// <param name="field">The public sort name, such as <c>domain</c>.</param>
    /// <param name="member">The member it orders by.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">The name is malformed or already registered.</exception>
    public SortMapBuilder<T> By(string field, Expression<Func<T, string>> member)
    {
        return Register(field, new TypedSortField<T, string>(member, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Names the order a list answers when the caller names none.</summary>
    /// <param name="field">A field already registered with <c>By</c>.</param>
    /// <param name="direction">The default direction.</param>
    /// <returns>The last step, which takes the tie-breaker and builds the map.</returns>
    /// <exception cref="ArgumentException">The field is not registered.</exception>
    public DefaultedSortMapBuilder<T> DefaultTo(string field, SortDirection direction)
    {
        if (!_fields.ContainsKey(field))
        {
            throw new ArgumentException($"The default sort '{field}' names no registered field.", nameof(field));
        }

        return new DefaultedSortMapBuilder<T>(
            _fields.ToFrozenDictionary(StringComparer.Ordinal),
            new SortKey(field, direction));
    }

    /// <summary>Adds one field after checking its name.</summary>
    /// <param name="field">The public sort name.</param>
    /// <param name="sortField">The typed ordering.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">The name is malformed or already registered.</exception>
    private SortMapBuilder<T> Register(string field, SortField<T> sortField)
    {
        if (!SortKey.TryParse(field, out var parsed) || parsed.Field != field)
        {
            throw new ArgumentException($"'{field}' is not a well-formed sort field name.", nameof(field));
        }

        if (!_fields.TryAdd(field, sortField))
        {
            throw new ArgumentException($"The sort field '{field}' is registered twice.", nameof(field));
        }

        return this;
    }
}
