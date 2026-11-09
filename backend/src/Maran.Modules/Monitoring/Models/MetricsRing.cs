using Maran.Sdk.Contracts;

namespace Maran.Modules.Monitoring.Models;

/// <summary>
/// A fixed-capacity ring of live host readings: once full, each new reading overwrites the oldest, so
/// memory is bounded by the capacity however long the panel runs.
/// </summary>
/// <remarks>
/// Not thread-safe, on purpose: the one owner (<c>LiveMetricsFeed</c>) already serialises every access
/// under its own lock, and a second lock here would be a second thing to order against it. Readings
/// are expected in time order; the ring does not sort, because the poller is the only writer and takes
/// them in the order it took them.
/// </remarks>
public sealed class MetricsRing
{
    /// <summary>The slots; a slot is <c>null</c> until it is first written.</summary>
    private readonly LiveMetricsReading?[] _slots;

    /// <summary>The slot the next reading is written to.</summary>
    private int _next;

    /// <summary>How many slots hold a reading.</summary>
    private int _count;

    /// <summary>How many readings the ring holds at most.</summary>
    public int Capacity
    {
        get
        {
            return _slots.Length;
        }
    }

    /// <summary>How many readings it holds now.</summary>
    public int Count
    {
        get
        {
            return _count;
        }
    }

    /// <summary>Creates an empty ring.</summary>
    /// <param name="capacity">How many readings it holds at most; at least one.</param>
    public MetricsRing(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _slots = new LiveMetricsReading?[capacity];
    }

    /// <summary>Stores a reading, overwriting the oldest when the ring is full.</summary>
    /// <param name="reading">The reading to keep.</param>
    public void Add(LiveMetricsReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        _slots[_next] = reading;
        _next = (_next + 1) % _slots.Length;
        _count = Math.Min(_count + 1, _slots.Length);
    }

    /// <summary>Returns the readings taken at or after <paramref name="since"/>, oldest first.</summary>
    /// <param name="since">The earliest instant wanted.</param>
    /// <returns>The matching readings; empty when none is that recent.</returns>
    public IReadOnlyList<LiveMetricsReading> Since(DateTimeOffset since)
    {
        var readings = new List<LiveMetricsReading>(_count);
        var oldest = _count == _slots.Length ? _next : 0;

        for (var offset = 0; offset < _count; offset++)
        {
            var reading = _slots[(oldest + offset) % _slots.Length]!;
            if (reading.At >= since)
            {
                readings.Add(reading);
            }
        }

        return readings;
    }

    /// <summary>The instant of the oldest reading held, or <c>null</c> when the ring is empty.</summary>
    /// <returns>The oldest reading's instant.</returns>
    public DateTimeOffset? OldestAt()
    {
        if (_count == 0)
        {
            return null;
        }

        var oldest = _count == _slots.Length ? _next : 0;
        return _slots[oldest]!.At;
    }
}
