namespace Maran.SharedKernel.Tests.Fixtures;

/// <summary>
/// A row the pager tests order and page: an id, a name to sort and search, a rank that ties, and a
/// date that does not.
/// </summary>
public sealed class PagedRow
{
    /// <summary>Unique id; the tie-breaker.</summary>
    public int Id { get; set; }

    /// <summary>A text field the tests sort and search by.</summary>
    public string Name { get; set; } = "";

    /// <summary>A number many rows share, so ordering by it alone is not total.</summary>
    public int Rank { get; set; }

    /// <summary>A date unique per row, so the map orders a non-text, non-number member as itself.</summary>
    public DateTimeOffset At { get; set; }
}
