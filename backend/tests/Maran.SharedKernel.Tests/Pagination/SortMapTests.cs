using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;
using Maran.SharedKernel.Tests.Fixtures;

namespace Maran.SharedKernel.Tests.Pagination;

/// <summary>Behavioural contract of <see cref="SortMap{T}"/>.</summary>
public sealed class SortMapTests
{
    /// <summary>The default order and every key of the map, either direction, are accepted.</summary>
    /// <param name="sort">An acceptable sort; empty for the default order.</param>
    [Theory]
    [InlineData("")]
    [InlineData("name")]
    [InlineData("-name")]
    [InlineData("rank")]
    [InlineData("-rank")]
    [InlineData("at")]
    [InlineData("-at")]
    public void The_default_order_and_every_mapped_key_are_accepted(string sort)
    {
        Assert.True(PagedRowSorts.Map.Accepts(SortRequest.Parse(sort)));
    }

    /// <summary>A well-formed key naming no field of the map is refused, and so is a differently cased one.</summary>
    /// <param name="sort">An unacceptable sort.</param>
    [Theory]
    [InlineData("id")]
    [InlineData("Name")]
    [InlineData("-password")]
    public void A_key_the_map_does_not_name_is_refused(string sort)
    {
        var request = SortRequest.Parse(sort);

        Assert.False(PagedRowSorts.Map.Accepts(request));
        Assert.Throws<ArgumentException>(() => { PagedRowSorts.Map.Resolve(request); });
    }

    /// <summary>The default order resolves to the map's default.</summary>
    [Fact]
    public void The_default_order_resolves_to_the_default()
    {
        Assert.Equal(new SortKey("name", SortDirection.Ascending), PagedRowSorts.Map.Resolve(SortRequest.Default));
    }

    /// <summary>The map lists exactly the fields it was built with.</summary>
    [Fact]
    public void The_map_lists_exactly_its_fields()
    {
        Assert.Equal(["at", "name", "rank"], PagedRowSorts.Map.Fields.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Text orders ordinally ignoring case in memory, so the order does not move with the server's culture.
    /// </summary>
    [Fact]
    public void Text_orders_ordinally_ignoring_case_in_memory()
    {
        var rows = new[]
        {
            new PagedRow { Id = 1, Name = "bravo" },
            new PagedRow { Id = 2, Name = "Alpha" },
            new PagedRow { Id = 3, Name = "charlie" },
        };

        var ordered = PagedRowSorts.Map.Order(rows, SortRequest.Parse("name"));

        Assert.Equal(["Alpha", "bravo", "charlie"], ordered.Select(row => { return row.Name; }));
    }

    /// <summary>
    /// A descending order over ties is exactly the ascending order reversed: the tie-breaker follows
    /// the chosen direction rather than staying ascending.
    /// </summary>
    [Fact]
    public void A_descending_order_over_ties_is_the_ascending_order_reversed()
    {
        var rows = PagedRowSorts.Rows(20);

        var ascending = PagedRowSorts.Map.Order(rows, SortRequest.Parse("rank")).Select(row => { return row.Id; });
        var descending = PagedRowSorts.Map.Order(rows, SortRequest.Parse("-rank")).Select(row => { return row.Id; });

        Assert.Equal(Enumerable.Range(1, 20), ascending);
        Assert.Equal(ascending.Reverse(), descending);
    }

    /// <summary>
    /// Ties within a non-unique field are broken in the chosen direction while the field itself still
    /// orders the rows: two ranks, each tied, descending.
    /// </summary>
    [Fact]
    public void Ties_inside_each_value_are_broken_in_the_chosen_direction()
    {
        var rows = new[]
        {
            new PagedRow { Id = 1, Rank = 1 },
            new PagedRow { Id = 2, Rank = 2 },
            new PagedRow { Id = 3, Rank = 1 },
            new PagedRow { Id = 4, Rank = 2 },
        };

        var ordered = PagedRowSorts.Map.Order(rows, SortRequest.Parse("-rank")).Select(row => { return row.Id; });

        Assert.Equal([4, 2, 3, 1], ordered);
    }
}
