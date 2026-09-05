using Maran.Modules.Monitoring.Domain.Policies;
using Maran.Modules.Monitoring.Models;

namespace Maran.Modules.Monitoring.Tests.Common;

/// <summary>
/// R7's arithmetic: a rate is a difference of counters divided by the seconds that actually elapsed,
/// and a difference that comes out negative is not a rate at all.
/// </summary>
public sealed class NetworkRateCalculatorTests
{
    /// <summary>The instant the first bucket in every fixture starts.</summary>
    private static readonly DateTimeOffset Start = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The first bucket has no rate, because a rate needs two readings.</summary>
    [Fact]
    public void The_first_bucket_has_no_rate_because_a_rate_needs_two_readings()
    {
        var rates = NetworkRateCalculator.RatesFor([Bucket(0, 1_000, 2_000)]);

        Assert.Single(rates);
        Assert.Null(rates[0]);
    }

    /// <summary>
    /// The divisor is the measured gap between two readings, not the sampler's nominal interval — so a
    /// sampler that missed four minutes reports a fifth of the traffic, not five times it.
    /// </summary>
    [Fact]
    public void A_rate_is_divided_by_the_seconds_that_actually_elapsed_not_by_the_sampling_interval()
    {
        // Five minutes between readings, 300,000 bytes received. Divided by the measured 300 seconds
        // that is 1,000 B/s. Divided by an assumed 60-second interval it would be 5,000 — a five-fold
        // spike drawn on the chart of a panel that was merely restarting.
        var rates = NetworkRateCalculator.RatesFor(
        [
            Bucket(0, 1_000, 2_000),
            Bucket(300, 301_000, 302_000),
        ]);

        Assert.NotNull(rates[1]);
        Assert.Equal(1_000d, rates[1]!.ReceivedBytesPerSecond, precision: 6);
        Assert.Equal(1_000d, rates[1]!.SentBytesPerSecond, precision: 6);
    }

    /// <summary>
    /// A counter reset — a reboot, an interface removed — reports no traffic rather than a negative
    /// spike of billions of bytes per second.
    /// </summary>
    /// <remarks>
    /// This is the test the clamp mutation must kill. Without <c>Math.Max(…, 0)</c> the arithmetic
    /// produces a large negative number on exactly the day an operator is looking at the chart to
    /// find out what happened.
    /// </remarks>
    [Fact]
    public void A_counter_reset_between_two_buckets_reports_no_traffic_rather_than_a_negative_spike()
    {
        var rates = NetworkRateCalculator.RatesFor(
        [
            Bucket(0, 9_000_000_000, 8_000_000_000),
            Bucket(60, 1_200, 900),
        ]);

        Assert.NotNull(rates[1]);
        Assert.Equal(0d, rates[1]!.ReceivedBytesPerSecond);
        Assert.Equal(0d, rates[1]!.SentBytesPerSecond);
    }

    /// <summary>Two readings taken at the same instant produce no rate: nothing divides by zero.</summary>
    [Fact]
    public void Two_readings_taken_at_the_same_instant_produce_no_rate()
    {
        var rates = NetworkRateCalculator.RatesFor(
        [
            Bucket(0, 1_000, 2_000),
            Bucket(0, 5_000, 6_000),
        ]);

        Assert.Null(rates[1]);
    }

    /// <summary>Builds one bucket whose last reading sits a given number of seconds after the start.</summary>
    /// <param name="secondsAfterStart">How long after <see cref="Start"/> the bucket's last sample was taken.</param>
    /// <param name="receivedBytes">The received-bytes counter at that reading.</param>
    /// <param name="sentBytes">The sent-bytes counter at that reading.</param>
    /// <returns>The bucket.</returns>
    private static MetricBucketRow Bucket(int secondsAfterStart, long receivedBytes, long sentBytes)
    {
        var at = Start.AddSeconds(secondsAfterStart);
        return new MetricBucketRow(at, 0, 0, 0, 0, 0, 0, receivedBytes, sentBytes, at);
    }
}
