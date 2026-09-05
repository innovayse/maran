using Maran.Modules.Monitoring.Common;
using Maran.Modules.Monitoring.Domain.Enums;
using Maran.Modules.Monitoring.Domain.Policies;
using Maran.Modules.Monitoring.Domain.ValueObjects;
using Maran.Modules.Monitoring.Models;

namespace Maran.Modules.Monitoring.Mappers;

/// <summary>
/// Assembles the chart's read model from the rows PostgreSQL bucketed and the rates
/// <see cref="NetworkRateCalculator"/> derived from them.
/// </summary>
/// <remarks>
/// Its own type rather than a private method on the query handler, and that is what makes R7
/// testable: the handler cannot run without a real PostgreSQL (its query is raw SQL using
/// <c>date_bin</c>), while everything interesting about the ANSWER — the clamped counter reset, the
/// measured elapsed time across a sampler gap, the missing first rate — happens here, over plain
/// objects, in a test that needs no container.
/// </remarks>
public static class MetricsChartMapper
{
    /// <summary>Builds the chart from its bucketed rows.</summary>
    /// <param name="range">The range the caller asked for, echoed into the result.</param>
    /// <param name="rows">The bucketed rows in ascending time order.</param>
    /// <returns>The chart, with one point per row.</returns>
    /// <remarks>
    /// The byte means are rounded to whole bytes on the way out. They are averages, so PostgreSQL
    /// hands back fractions of a byte, and a chart axis labelled in gibibytes has no use for them —
    /// while a fractional byte in a JSON payload is the kind of detail that makes a reader wonder
    /// what it means.
    /// </remarks>
    public static MetricsChartDto Create(ChartRange range, IReadOnlyList<MetricBucketRow> rows)
    {
        var rates = NetworkRateCalculator.RatesFor(rows);
        var buckets = new List<MetricBucketDto>(rows.Count);

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var rate = rates[index];

            buckets.Add(new MetricBucketDto(
                row.BucketStart,
                row.CpuPercent,
                (long)Math.Round(row.MemoryUsedBytes),
                (long)Math.Round(row.MemoryTotalBytes),
                (long)Math.Round(row.DiskUsedBytes),
                (long)Math.Round(row.DiskTotalBytes),
                row.LoadAverage1m,
                rate?.ReceivedBytesPerSecond,
                rate?.SentBytesPerSecond));
        }

        return new MetricsChartDto(range, (int)ChartWindow.For(range).Bucket.TotalSeconds, buckets);
    }
}
