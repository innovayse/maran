using System.Globalization;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.SharedKernel.Tests.Fixtures;

/// <summary>The sort map and data the pager tests share.</summary>
public static class PagedRowSorts
{
    /// <summary>
    /// The map: <c>name</c> (text), <c>rank</c> (a number) and <c>at</c> (a date), defaulting to
    /// <c>name</c> ascending, ties broken by id.
    /// </summary>
    public static readonly SortMap<PagedRow> Map = new SortMapBuilder<PagedRow>()
        .By("name", row => row.Name)
        .By("rank", row => row.Rank)
        .By("at", row => row.At)
        .DefaultTo("name", SortDirection.Ascending)
        .TieBreaker(row => row.Id);

    /// <summary>The instant row 1 is stamped with; every later row is one hour after the one before.</summary>
    public static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Builds <paramref name="count"/> rows with ids 1..count, names <c>row-001</c>.., every rank 7
    /// so a rank sort is all ties, each stamped an hour after the one before, inserted in reverse id
    /// order so insertion order cannot pass for the tie-breaker.
    /// </summary>
    /// <param name="count">How many rows.</param>
    /// <returns>The rows.</returns>
    public static List<PagedRow> Rows(int count)
    {
        return Enumerable.Range(1, count)
            .Reverse()
            .Select(id =>
            {
                return new PagedRow
                {
                    Id = id,
                    Name = "row-" + id.ToString("D3", CultureInfo.InvariantCulture),
                    Rank = 7,
                    At = Epoch.AddHours(id),
                };
            })
            .ToList();
    }
}
