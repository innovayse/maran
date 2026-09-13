using Maran.Modules.Monitoring.Domain.Enums;
using Maran.Modules.Monitoring.Mappers;
using Maran.Modules.Monitoring.Models;

namespace Maran.Modules.Monitoring.Tests.Common;

/// <summary>
/// The chart read model assembled from bucketed rows — the place R7's two hard cases, a sampler gap
/// and a counter reset, become the numbers an operator actually sees.
/// </summary>
public sealed class MetricsChartDtoFactoryTests
{
    /// <summary>The instant the fixture's first bucket starts.</summary>
    private static readonly DateTimeOffset Start = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A series carrying both a sampler gap and a counter reset yields a rate measured against the
    /// real elapsed time and a clamped zero, and never a negative or an inflated one.
    /// </summary>
    /// <remarks>
    /// Both hazards in one series on purpose: they are the two ways the same subtraction goes wrong,
    /// and a chart draws them side by side.
    /// </remarks>
    [Fact]
    public void Bucketing_a_gap_and_a_counter_reset_yields_a_measured_rate_and_a_clamped_zero()
    {
        var chart = MetricsChartMapper.Create(ChartRange.LastDay,
        [
            Bucket(0, 10_000, 20_000),

            // The gap: nine minutes with no sample, then 540,000 bytes more. 540,000 / 540 = 1,000 B/s.
            // Divided by a nominal five-minute bucket it would read as 1,800.
            Bucket(540, 550_000, 560_000),

            // The reset: the host rebooted, so the counters are smaller than they were.
            Bucket(600, 300, 400),
        ]);

        Assert.Equal(ChartRange.LastDay, chart.Range);
        Assert.Equal(300, chart.BucketSeconds);
        Assert.Equal(3, chart.Buckets.Count);

        Assert.Null(chart.Buckets[0].NetworkReceivedBytesPerSecond);
        Assert.Null(chart.Buckets[0].NetworkSentBytesPerSecond);

        Assert.Equal(1_000d, chart.Buckets[1].NetworkReceivedBytesPerSecond!.Value, precision: 6);
        Assert.Equal(1_000d, chart.Buckets[1].NetworkSentBytesPerSecond!.Value, precision: 6);

        Assert.Equal(0d, chart.Buckets[2].NetworkReceivedBytesPerSecond!.Value);
        Assert.Equal(0d, chart.Buckets[2].NetworkSentBytesPerSecond!.Value);
    }

    /// <summary>The seven-day range is echoed back with its own, coarser bucket width.</summary>
    [Fact]
    public void The_week_range_is_echoed_back_with_its_own_bucket_width()
    {
        var chart = MetricsChartMapper.Create(ChartRange.LastWeek, []);

        Assert.Equal(ChartRange.LastWeek, chart.Range);
        Assert.Equal(1_800, chart.BucketSeconds);
        Assert.Empty(chart.Buckets);
    }

    /// <summary>Byte means are rounded to whole bytes, because a fraction of a byte means nothing on an axis.</summary>
    [Fact]
    public void Byte_means_are_rounded_to_whole_bytes()
    {
        var row = new MetricBucketRow(Start, 12.5, 1_024.6, 2_048.4, 4_096.5, 8_192.5, 0.75, 0, 0, Start);

        var chart = MetricsChartMapper.Create(ChartRange.LastDay, [row]);

        Assert.Equal(1_025, chart.Buckets[0].MemoryUsedBytes);
        Assert.Equal(2_048, chart.Buckets[0].MemoryTotalBytes);
        Assert.Equal(12.5, chart.Buckets[0].CpuPercent);
        Assert.Equal(0.75, chart.Buckets[0].LoadAverage1m);
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
