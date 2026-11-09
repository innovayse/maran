using Maran.Host.Demo.Guard;

namespace Maran.Host.Demo.Tests.Guard;

/// <summary>The visitor table is bounded and its sweep of expired windows is rationed.</summary>
public sealed class DemoVisitorWindowsTests
{
    /// <summary>The instant the tests start from.</summary>
    private static readonly DateTimeOffset Start = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

    /// <summary>The window length the tests use.</summary>
    private static readonly TimeSpan Length = TimeSpan.FromSeconds(60);

    /// <summary>A window that ended starts again from nothing.</summary>
    [Fact]
    public void An_ended_window_restarts_with_nothing_counted()
    {
        var windows = new DemoVisitorWindows(10, Length);
        windows.Current("a", Start).CountOne();

        var again = windows.Current("a", Start.AddSeconds(61));

        Assert.Equal(0, again.Count);
        Assert.Equal(Start.AddSeconds(61), again.Start);
    }

    /// <summary>Visitors beyond the bound share the overflow window.</summary>
    [Fact]
    public void Visitors_beyond_the_bound_share_one_overflow_window()
    {
        var windows = new DemoVisitorWindows(2, Length);
        windows.Current("a", Start);
        windows.Current("b", Start);

        var first = windows.Current("c", Start);
        var second = windows.Current("d", Start);

        Assert.Same(first, second);
        Assert.Equal(3, windows.Count);
    }

    /// <summary>The sweep control: a full table is walked once per window length, not once per new key.</summary>
    [Fact]
    public void A_full_table_is_swept_at_most_once_per_window()
    {
        var windows = new DemoVisitorWindows(2, Length);
        windows.Current("a", Start);
        windows.Current("b", Start);

        // Both windows ended by now, so the first new key sweeps them and finds room.
        windows.Current("c", Start.AddSeconds(61));
        Assert.Equal(1, windows.Count);

        // Refill; "c" and "d" end at 61 + 60 = 121 s and 62 + 60 = 122 s.
        windows.Current("d", Start.AddSeconds(62));
        Assert.Equal(2, windows.Count);

        // At 125 s both are ended, but the last sweep was at 61 s, within one window length, so it does not run:
        // the newcomer lands in the overflow window instead of walking the table again.
        windows.Current("e", Start.AddSeconds(100));
        Assert.Equal(3, windows.Count);

        // A full window length after the last sweep it runs again and finds the room.
        windows.Current("f", Start.AddSeconds(125));
        Assert.Equal(2, windows.Count);
    }
}
