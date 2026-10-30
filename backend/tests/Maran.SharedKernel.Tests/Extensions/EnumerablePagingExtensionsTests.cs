using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Pagination;
using Maran.SharedKernel.Tests.Fixtures;

namespace Maran.SharedKernel.Tests.Extensions;

/// <summary>Behavioural contract of <see cref="EnumerablePagingExtensions"/>.</summary>
public sealed class EnumerablePagingExtensionsTests
{
    /// <summary>The members a search looks in.</summary>
    private static readonly IReadOnlyList<Func<PagedRow, string>> SearchFields = [row => { return row.Name; }];

    /// <summary>The first page carries the page size's worth of rows and the total of every row.</summary>
    [Fact]
    public void The_first_page_carries_its_rows_and_the_whole_total()
    {
        var page = PagedRowSorts.Rows(60).ToPaged(new PageRequest(1, 25), PagedRowSorts.Map, SearchFields);

        Assert.Equal(60, page.Total);
        Assert.Equal(25, page.Items.Count);
        Assert.Equal("row-001", page.Items[0].Name);
        Assert.Equal(1, page.Page);
        Assert.Equal(25, page.PageSize);
    }

    /// <summary>The last page carries only the remainder.</summary>
    [Fact]
    public void The_last_page_carries_the_remainder()
    {
        var page = PagedRowSorts.Rows(60).ToPaged(new PageRequest(3, 25), PagedRowSorts.Map, SearchFields);

        Assert.Equal(60, page.Total);
        Assert.Equal(Enumerable.Range(51, 10), page.Items.Select(row => { return row.Id; }));
    }

    /// <summary>A page past the end is empty, keeps the true total, and echoes the page asked for.</summary>
    [Fact]
    public void A_page_past_the_end_is_empty_with_the_true_total()
    {
        var page = PagedRowSorts.Rows(60).ToPaged(new PageRequest(9, 25), PagedRowSorts.Map, SearchFields);

        Assert.Empty(page.Items);
        Assert.Equal(60, page.Total);
        Assert.Equal(9, page.Page);
    }

    /// <summary>A page so far past the end that its offset overflows 32 bits is still just empty.</summary>
    [Fact]
    public void A_huge_page_number_does_not_overflow()
    {
        var page = PagedRowSorts.Rows(3).ToPaged(new PageRequest(int.MaxValue, 100), PagedRowSorts.Map, SearchFields);

        Assert.Empty(page.Items);
        Assert.Equal(3, page.Total);
    }

    /// <summary>
    /// Under an order that is all ties, the tie-breaker makes consecutive pages disjoint and complete,
    /// and under a descending order it breaks the ties descending — the tie-breaker follows the
    /// direction (see <see cref="SortMap{T}"/>).
    /// </summary>
    /// <param name="sort">An order that is all ties.</param>
    /// <param name="descending">Whether the ids are expected highest first.</param>
    [Theory]
    [InlineData("rank", false)]
    [InlineData("-rank", true)]
    public void Ties_are_broken_by_the_tie_breaker_so_pages_neither_repeat_nor_skip(string sort, bool descending)
    {
        var rows = PagedRowSorts.Rows(30);

        var seen = Enumerable.Range(1, 3)
            .SelectMany(number =>
            {
                return rows.ToPaged(new PageRequest(number, 10, Sort: SortRequest.Parse(sort)), PagedRowSorts.Map, SearchFields).Items;
            })
            .Select(row => { return row.Id; })
            .ToList();

        var expected = Enumerable.Range(1, 30);
        Assert.Equal(descending ? expected.Reverse() : expected, seen);
    }

    /// <summary>A date member orders as a date, in both directions.</summary>
    [Fact]
    public void A_date_member_orders_as_a_date_in_both_directions()
    {
        var rows = PagedRowSorts.Rows(12);

        var ascending = rows.ToPaged(new PageRequest(Sort: SortRequest.Parse("at")), PagedRowSorts.Map, SearchFields);
        var descending = rows.ToPaged(new PageRequest(Sort: SortRequest.Parse("-at")), PagedRowSorts.Map, SearchFields);

        Assert.Equal(Enumerable.Range(1, 12), ascending.Items.Select(row => { return row.Id; }));
        Assert.Equal(Enumerable.Range(1, 12).Reverse(), descending.Items.Select(row => { return row.Id; }));
    }

    /// <summary>A sort the map does not name reaching the pager is a programming error, never a silent default.</summary>
    [Fact]
    public void An_unvalidated_sort_throws_rather_than_falling_back()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            PagedRowSorts.Rows(3).ToPaged(new PageRequest(Sort: SortRequest.Parse("id")), PagedRowSorts.Map, SearchFields);
        });
    }

    /// <summary>A descending sort reverses the order.</summary>
    [Fact]
    public void A_descending_sort_reverses_the_order()
    {
        var page = PagedRowSorts.Rows(5).ToPaged(new PageRequest(Sort: SortRequest.Parse("-name")), PagedRowSorts.Map, SearchFields);

        Assert.Equal([5, 4, 3, 2, 1], page.Items.Select(row => { return row.Id; }));
    }

    /// <summary>A search keeps the matching rows, ignoring case, and the total counts only them.</summary>
    [Fact]
    public void A_search_narrows_the_rows_and_the_total()
    {
        var page = PagedRowSorts.Rows(30).ToPaged(new PageRequest(Search: SearchTerm.Of("ROW-01")), PagedRowSorts.Map, SearchFields);

        Assert.Equal(10, page.Total);
        Assert.All(page.Items, row => { Assert.StartsWith("row-01", row.Name, StringComparison.Ordinal); });
    }

    /// <summary>A trimmed term matches as its text: the spaces around it are not part of the search.</summary>
    [Fact]
    public void A_term_is_matched_without_the_spaces_around_it()
    {
        var page = PagedRowSorts.Rows(30).ToPaged(new PageRequest(Search: SearchTerm.Of("  row-01  ")), PagedRowSorts.Map, SearchFields);

        Assert.Equal(10, page.Total);
    }

    /// <summary>No search matches every row.</summary>
    [Fact]
    public void No_search_matches_every_row()
    {
        var page = PagedRowSorts.Rows(30).ToPaged(new PageRequest(Search: SearchTerm.None), PagedRowSorts.Map, SearchFields);

        Assert.Equal(30, page.Total);
    }

    /// <summary>A search term is matched literally; a wildcard character is not a wildcard here.</summary>
    [Fact]
    public void A_search_term_is_matched_literally()
    {
        var page = PagedRowSorts.Rows(30).ToPaged(new PageRequest(Search: SearchTerm.Of("%")), PagedRowSorts.Map, SearchFields);

        Assert.Equal(0, page.Total);
        Assert.Empty(page.Items);
    }

    /// <summary>A page size outside the allowed set is a programming error, never clamped.</summary>
    [Fact]
    public void An_unvalidated_page_size_throws_rather_than_clamping()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            PagedRowSorts.Rows(3).ToPaged(new PageRequest(1, 37), PagedRowSorts.Map, SearchFields);
        });
    }
}
