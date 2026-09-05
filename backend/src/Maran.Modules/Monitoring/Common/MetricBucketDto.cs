namespace Maran.Modules.Monitoring.Common;

/// <summary>One point on the panel's charts: a bucket of raw samples, already reduced.</summary>
/// <remarks>
/// The level metrics are means across the bucket. The two network figures are RATES derived from
/// the counters (R7) and are nullable, because the first bucket of any chart has no earlier reading
/// to measure against — a gap the interface draws as a gap, never as a minute of zero traffic.
/// </remarks>
/// <param name="At">The instant the bucket begins.</param>
/// <param name="CpuPercent">Mean processor utilisation across the bucket, 0.0-100.0.</param>
/// <param name="MemoryUsedBytes">Mean memory in use across the bucket.</param>
/// <param name="MemoryTotalBytes">Mean installed memory across the bucket.</param>
/// <param name="DiskUsedBytes">Mean disk space in use across the bucket.</param>
/// <param name="DiskTotalBytes">Mean root filesystem capacity across the bucket.</param>
/// <param name="LoadAverage1m">Mean one-minute load average across the bucket.</param>
/// <param name="NetworkReceivedBytesPerSecond">Mean bytes received per second, or <c>null</c> for the first bucket.</param>
/// <param name="NetworkSentBytesPerSecond">Mean bytes sent per second, or <c>null</c> for the first bucket.</param>
public sealed record MetricBucketDto(
    DateTimeOffset At,
    double CpuPercent,
    long MemoryUsedBytes,
    long MemoryTotalBytes,
    long DiskUsedBytes,
    long DiskTotalBytes,
    double LoadAverage1m,
    double? NetworkReceivedBytesPerSecond,
    double? NetworkSentBytesPerSecond);
