using Maran.Modules.Monitoring.Models;
namespace Maran.Modules.Monitoring.Domain.Policies;

/// <summary>
/// Turns a series of network COUNTERS into a series of rates — the one piece of arithmetic R7 is
/// about, kept in a pure function so it can be read, tested and broken on purpose without a
/// database.
/// </summary>
/// <remarks>
/// <para>
/// <b>The divisor is measured, never assumed.</b> The sampler runs about every sixty seconds and is
/// explicitly allowed to miss: the agent may be down, the panel may have been restarting, the host
/// may have been too busy. Dividing by a constant sixty would then report a five-minute gap's worth
/// of traffic as if it had happened in one minute — a five-fold spike drawn on a chart an operator
/// is using to decide whether something is wrong. So the divisor is the difference between the two
/// readings' own timestamps.
/// </para>
/// <para>
/// <b>A negative difference is clamped to zero, and it is not a defensive nicety.</b> The counters
/// restart at zero when the host reboots, and they drop when an interface is removed — both of which
/// make the newer reading SMALLER than the older one. Without the clamp the subtraction is negative
/// and the chart draws a downward spike of billions of bytes per second, on exactly the day
/// (a reboot) when somebody is looking at it to find out what happened.
/// </para>
/// <para>
/// <b>The first bucket has no rate, and gets none.</b> A rate needs two readings. Emitting zero for
/// the first would draw a dip at the left edge of every chart that is indistinguishable from a
/// minute of genuine silence, so the answer is the absence of a value — which the read model carries
/// as <c>null</c> and the interface renders as a gap.
/// </para>
/// </remarks>
public static class NetworkRateCalculator
{
    /// <summary>Derives one rate per bucket from the buckets' counter readings.</summary>
    /// <param name="buckets">The buckets in ascending time order, as the chart's SQL returns them.</param>
    /// <returns>
    /// One entry per bucket, aligned by index. The first is always <c>null</c>; a later one is
    /// <c>null</c> when the two readings carry the same instant, which no division can survive.
    /// </returns>
    public static IReadOnlyList<NetworkRate?> RatesFor(IReadOnlyList<MetricBucketRow> buckets)
    {
        var rates = new NetworkRate?[buckets.Count];

        for (var index = 1; index < buckets.Count; index++)
        {
            rates[index] = Between(buckets[index - 1], buckets[index]);
        }

        return rates;
    }

    /// <summary>Derives the rate between two consecutive buckets' last readings.</summary>
    /// <param name="previous">The earlier bucket.</param>
    /// <param name="current">The later bucket.</param>
    /// <returns>The rate, or <c>null</c> when no time separates the two readings.</returns>
    /// <remarks>
    /// The non-positive-elapsed guard covers two real cases and not a hypothetical one: two buckets
    /// can hold the same last reading when a bucket contains exactly one sample and the sampler
    /// stalled, and PostgreSQL will happily return equal timestamps for two rows written inside the
    /// same clock tick. Dividing by that yields an infinity that serialises as <c>NaN</c> and paints
    /// nothing.
    /// </remarks>
    private static NetworkRate? Between(MetricBucketRow previous, MetricBucketRow current)
    {
        var elapsed = (current.LastCapturedAt - previous.LastCapturedAt).TotalSeconds;

        if (elapsed <= 0)
        {
            return null;
        }

        var received = Math.Max(current.NetworkRxBytes - previous.NetworkRxBytes, 0L);
        var sent = Math.Max(current.NetworkTxBytes - previous.NetworkTxBytes, 0L);

        return new NetworkRate(received / elapsed, sent / elapsed);
    }
}
