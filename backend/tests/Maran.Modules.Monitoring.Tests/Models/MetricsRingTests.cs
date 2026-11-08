using Maran.Modules.Monitoring.Models;
using Maran.Modules.Monitoring.Tests.TestSupport;

namespace Maran.Modules.Monitoring.Tests.Models;

/// <summary>The fixed-capacity ring behind the live feed: bounded, ordered, and honest when empty.</summary>
public sealed class MetricsRingTests
{
    /// <summary>The instant the readings below are counted from.</summary>
    private static readonly DateTimeOffset Start = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>An empty ring holds nothing and has no oldest instant.</summary>
    [Fact]
    public void An_empty_ring_holds_nothing_and_has_no_oldest_instant()
    {
        var ring = new MetricsRing(3);

        Assert.Equal(0, ring.Count);
        Assert.Null(ring.OldestAt());
        Assert.Empty(ring.Since(Start));
    }

    /// <summary>A ring below capacity returns what it was given, oldest first.</summary>
    [Fact]
    public void A_ring_below_capacity_returns_what_it_was_given_oldest_first()
    {
        var ring = new MetricsRing(3);
        ring.Add(LiveReadings.At(Start, 1));
        ring.Add(LiveReadings.At(Start.AddSeconds(2), 2));

        var all = ring.Since(Start);

        Assert.Equal([1d, 2d], all.Select(reading =>
        {
            return reading.Cpu.TotalPercent;
        }));
        Assert.Equal(Start, ring.OldestAt());
    }

    /// <summary>A full ring overwrites its oldest reading and never grows past its capacity.</summary>
    [Fact]
    public void A_full_ring_overwrites_its_oldest_reading_and_never_grows_past_its_capacity()
    {
        var ring = new MetricsRing(3);
        for (var index = 0; index < 5; index++)
        {
            ring.Add(LiveReadings.At(Start.AddSeconds(index * 2), index));
        }

        var all = ring.Since(Start);

        Assert.Equal(3, ring.Count);
        Assert.Equal([2d, 3d, 4d], all.Select(reading =>
        {
            return reading.Cpu.TotalPercent;
        }));
        Assert.Equal(Start.AddSeconds(4), ring.OldestAt());
    }

    /// <summary>The time filter keeps readings at or after the instant and drops the earlier ones.</summary>
    [Fact]
    public void The_time_filter_keeps_readings_at_or_after_the_instant_and_drops_the_earlier_ones()
    {
        var ring = new MetricsRing(10);
        for (var index = 0; index < 4; index++)
        {
            ring.Add(LiveReadings.At(Start.AddSeconds(index * 2), index));
        }

        var recent = ring.Since(Start.AddSeconds(4));

        Assert.Equal([2d, 3d], recent.Select(reading =>
        {
            return reading.Cpu.TotalPercent;
        }));
    }

    /// <summary>A ring cannot be built with no room for a reading.</summary>
    [Fact]
    public void A_ring_cannot_be_built_with_no_room_for_a_reading()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            return new MetricsRing(0);
        });
    }
}
