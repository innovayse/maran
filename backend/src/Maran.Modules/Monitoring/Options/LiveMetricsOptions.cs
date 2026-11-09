using System.ComponentModel.DataAnnotations;

namespace Maran.Modules.Monitoring.Options;

/// <summary>
/// Tuning for the shared live-metrics feed behind the dashboard stream. Bound from the
/// <c>LiveMetrics</c> configuration section and validated at startup, so a nonsensical cadence fails
/// the boot rather than the first dashboard.
/// </summary>
/// <remarks>
/// The defaults are the issue's figures: a reading every two seconds, and a ring that holds an hour of
/// them (<see cref="RingCapacity"/> × <see cref="IntervalMilliseconds"/> = 3,600 seconds). The two are
/// separate settings because a deployment may change the cadence without wanting the ring's memory to
/// change with it; the history window the feed answers for is the ring's reach, not a third number.
/// </remarks>
public sealed class LiveMetricsOptions
{
    /// <summary>Configuration section this type binds from.</summary>
    public const string SectionName = "LiveMetrics";

    /// <summary>
    /// How long between two polls of the agent while someone is watching, in milliseconds. Each poll
    /// holds the agent for about a quarter of a second (it measures processor use across two readings),
    /// so the floor keeps polls from overlapping the next one's start.
    /// </summary>
    [Range(1_000, 60_000)]
    public int IntervalMilliseconds { get; set; } = 2_000;

    /// <summary>How many readings the in-memory ring holds before the oldest is overwritten.</summary>
    [Range(60, 20_000)]
    public int RingCapacity { get; set; } = 1_800;

    /// <summary>
    /// How long the feed keeps polling after its last subscriber has left, in seconds. A tab reload or a
    /// reconnect drops its subscription for a moment, and polling that stopped and restarted on every one
    /// would leave a hole in the ring exactly where the reconnecting client asks for back-fill.
    /// </summary>
    [Range(0, 600)]
    public int IdleStopSeconds { get; set; } = 30;

    /// <summary><see cref="IntervalMilliseconds"/> as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan Interval
    {
        get
        {
            return TimeSpan.FromMilliseconds(IntervalMilliseconds);
        }
    }

    /// <summary><see cref="IdleStopSeconds"/> as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan IdleStop
    {
        get
        {
            return TimeSpan.FromSeconds(IdleStopSeconds);
        }
    }
}
