using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Pagination;
using Maran.SharedKernel.Tests.Fixtures;

namespace Maran.SharedKernel.Tests.Extensions;

/// <summary>
/// Behavioural contract of <see cref="QueryablePagingExtensions"/>, through a real EF Core query provider.
/// </summary>
public sealed class QueryablePagingExtensionsTests
{
    /// <summary>
    /// The first page carries the page size's worth of rows, in the default order, and the total of every row.
    /// </summary>
    [Fact]
    public async Task The_first_page_carries_its_rows_and_the_whole_total()
    {
        using var context = await SeededAsync(60);

        var page = await context.Rows.ToPagedAsync(new PageRequest(1, 25), PagedRowSorts.Map, CancellationToken.None);

        Assert.Equal(60, page.Total);
        Assert.Equal(Enumerable.Range(1, 25), page.Items.Select(row => { return row.Id; }));
    }

    /// <summary>The last page carries only the remainder.</summary>
    [Fact]
    public async Task The_last_page_carries_the_remainder()
    {
        using var context = await SeededAsync(60);

        var page = await context.Rows.ToPagedAsync(new PageRequest(3, 25), PagedRowSorts.Map, CancellationToken.None);

        Assert.Equal(60, page.Total);
        Assert.Equal(Enumerable.Range(51, 10), page.Items.Select(row => { return row.Id; }));
    }

    /// <summary>A page past the end is empty, keeps the true total, and echoes the page asked for.</summary>
    [Fact]
    public async Task A_page_past_the_end_is_empty_with_the_true_total()
    {
        using var context = await SeededAsync(60);

        var page = await context.Rows.ToPagedAsync(new PageRequest(4, 25), PagedRowSorts.Map, CancellationToken.None);

        Assert.Empty(page.Items);
        Assert.Equal(60, page.Total);
        Assert.Equal(4, page.Page);
    }

    /// <summary>
    /// Under an order that is all ties, the tie-breaker makes consecutive pages disjoint and complete.
    /// </summary>
    [Fact]
    public async Task Ties_are_broken_by_the_tie_breaker_so_pages_neither_repeat_nor_skip()
    {
        using var context = await SeededAsync(30);

        var seen = new List<int>();
        foreach (var number in Enumerable.Range(1, 3))
        {
            var page = await context.Rows.ToPagedAsync(
                new PageRequest(number, 10, Sort: SortRequest.Parse("rank")),
                PagedRowSorts.Map,
                CancellationToken.None);
            seen.AddRange(page.Items.Select(row => { return row.Id; }));
        }

        Assert.Equal(Enumerable.Range(1, 30), seen);
    }

    /// <summary>
    /// A descending order breaks its ties descending too, so it is the ascending order read backwards —
    /// the tie-breaker follows the direction (see <see cref="SortMap{T}"/>).
    /// </summary>
    [Fact]
    public async Task A_descending_order_over_ties_is_the_ascending_order_reversed()
    {
        using var context = await SeededAsync(30);

        var ascending = await context.Rows.ToPagedAsync(
            new PageRequest(1, 50, Sort: SortRequest.Parse("rank")),
            PagedRowSorts.Map,
            CancellationToken.None);
        var descending = await context.Rows.ToPagedAsync(
            new PageRequest(1, 50, Sort: SortRequest.Parse("-rank")),
            PagedRowSorts.Map,
            CancellationToken.None);

        Assert.Equal(Enumerable.Range(1, 30), ascending.Items.Select(row => { return row.Id; }));
        Assert.Equal(Enumerable.Range(1, 30).Reverse(), descending.Items.Select(row => { return row.Id; }));
    }

    /// <summary>A date member orders as a date through the query provider, in both directions.</summary>
    [Fact]
    public async Task A_date_member_orders_as_a_date_in_both_directions()
    {
        using var context = await SeededAsync(12);

        var ascending = await context.Rows.ToPagedAsync(
            new PageRequest(Sort: SortRequest.Parse("at")),
            PagedRowSorts.Map,
            CancellationToken.None);
        var descending = await context.Rows.ToPagedAsync(
            new PageRequest(Sort: SortRequest.Parse("-at")),
            PagedRowSorts.Map,
            CancellationToken.None);

        Assert.Equal(Enumerable.Range(1, 12), ascending.Items.Select(row => { return row.Id; }));
        Assert.Equal(Enumerable.Range(1, 12).Reverse(), descending.Items.Select(row => { return row.Id; }));
    }

    /// <summary>The total counts what the caller's filter left, not the whole table.</summary>
    [Fact]
    public async Task The_total_counts_the_filtered_rows_only()
    {
        using var context = await SeededAsync(30);

        var page = await context.Rows
            .Where(row => row.Id > 20)
            .ToPagedAsync(new PageRequest(Sort: SortRequest.Parse("-name")), PagedRowSorts.Map, CancellationToken.None);

        Assert.Equal(10, page.Total);
        Assert.Equal(Enumerable.Range(21, 10).Reverse(), page.Items.Select(row => { return row.Id; }));
    }

    /// <summary>An unknown sort reaching the pager is a programming error, never a silent default.</summary>
    [Fact]
    public async Task An_unvalidated_sort_throws_rather_than_falling_back()
    {
        using var context = await SeededAsync(3);

        await Assert.ThrowsAsync<ArgumentException>(() =>
        {
            return context.Rows.ToPagedAsync(new PageRequest(Sort: SortRequest.Parse("id")), PagedRowSorts.Map, CancellationToken.None);
        });
    }

    /// <summary>Creates a private in-memory database holding <paramref name="count"/> rows.</summary>
    /// <param name="count">How many rows.</param>
    /// <returns>The seeded context; the caller disposes it.</returns>
    private static async Task<PagedRowContext> SeededAsync(int count)
    {
        var context = new PagedRowContext();
        context.Rows.AddRange(PagedRowSorts.Rows(count));
        await context.SaveChangesAsync();
        return context;
    }
}
