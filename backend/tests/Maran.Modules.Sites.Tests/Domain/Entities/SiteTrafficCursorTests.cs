using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Tests.TestSupport;

namespace Maran.Modules.Sites.Tests.Domain.Entities;

/// <summary>Behavioral contract of <see cref="SiteTrafficCursor"/>.</summary>
public sealed class SiteTrafficCursorTests
{
    /// <summary>A fixed instant, so nothing here reads the ambient clock.</summary>
    private static readonly DateTimeOffset First = new(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);

    /// <summary>A new cursor holds the position it was given and no gap.</summary>
    [Fact]
    public void A_new_cursor_holds_the_position_it_was_given_and_no_gap()
    {
        var cursor = NewCursor();

        Assert.Equal("1:2:abc", cursor.FileId);
        Assert.Equal(100, cursor.Offset);
        Assert.Equal(First, cursor.UpdatedAt);
        Assert.Null(cursor.IncompleteSince);
    }

    /// <summary>Advancing moves the position and the instant and leaves a recorded gap alone.</summary>
    [Fact]
    public void Advancing_moves_the_position_and_the_instant_and_leaves_a_recorded_gap_alone()
    {
        var cursor = NewCursor();
        cursor.RecordGap(First);

        cursor.Advance("3:4:def", 250, First.AddHours(1));

        Assert.Equal("3:4:def", cursor.FileId);
        Assert.Equal(250, cursor.Offset);
        Assert.Equal(First.AddHours(1), cursor.UpdatedAt);
        Assert.Equal(First, cursor.IncompleteSince);
    }

    /// <summary>A later gap replaces the earlier one.</summary>
    [Fact]
    public void A_later_gap_replaces_the_earlier_one()
    {
        var cursor = NewCursor();

        cursor.RecordGap(First);
        cursor.RecordGap(First.AddDays(2));

        Assert.Equal(First.AddDays(2), cursor.IncompleteSince);
    }

    /// <summary>Builds a cursor on a fresh site.</summary>
    /// <returns>The cursor.</returns>
    private static SiteTrafficCursor NewCursor()
    {
        return new SiteTrafficCursor(SitesTestContext.PhpSite(Guid.NewGuid(), "example.com"), "1:2:abc", 100, First);
    }
}
