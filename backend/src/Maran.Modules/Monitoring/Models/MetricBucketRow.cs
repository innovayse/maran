namespace Maran.Modules.Monitoring.Models;

/// <summary>
/// One bucket exactly as the chart's SQL produces it: the level metrics already averaged by
/// PostgreSQL, and the network counters left as counters.
/// </summary>
/// <remarks>
/// <para>
/// <b>The network columns are the LAST reading inside the bucket, with the instant it was taken.</b>
/// They are not averaged, and averaging them would be meaningless — the mean of a monotonically
/// rising counter is a number with no physical interpretation. The rate is derived afterwards, in
/// <see cref="Maran.Modules.Monitoring.Domain.Policies.NetworkRateCalculator"/>, from consecutive buckets' last readings and the seconds that
/// actually separate them (R7).
/// </para>
/// <para>
/// <b>Every byte total arrives as a <see cref="double"/>.</b> They are averages of a
/// <c>bigint</c> column and PostgreSQL returns <c>numeric</c> for those; the SQL casts to
/// <c>double precision</c> so the value crosses as a floating-point number rather than a decimal,
/// which is the right shape for something that is about to be drawn on a chart anyway.
/// </para>
/// <para>
/// This is a keyless read model, mapped in <c>MetricBucketRowConfiguration</c> so EF Core can
/// materialise it from raw SQL. It is never inserted, never tracked, and has no table.
/// </para>
/// </remarks>
/// <param name="BucketStart">The instant the bucket begins, as <c>date_bin</c> placed it.</param>
/// <param name="CpuPercent">Mean processor utilisation across the bucket.</param>
/// <param name="MemoryUsedBytes">Mean memory in use across the bucket.</param>
/// <param name="MemoryTotalBytes">Mean installed memory across the bucket.</param>
/// <param name="DiskUsedBytes">Mean disk space in use across the bucket.</param>
/// <param name="DiskTotalBytes">Mean root filesystem capacity across the bucket.</param>
/// <param name="LoadAverage1m">Mean one-minute load average across the bucket.</param>
/// <param name="NetworkRxBytes">The last received-bytes counter in the bucket.</param>
/// <param name="NetworkTxBytes">The last sent-bytes counter in the bucket.</param>
/// <param name="LastCapturedAt">When that last reading was taken — the clock the rate is divided by.</param>
public sealed record MetricBucketRow(
    DateTimeOffset BucketStart,
    double CpuPercent,
    double MemoryUsedBytes,
    double MemoryTotalBytes,
    double DiskUsedBytes,
    double DiskTotalBytes,
    double LoadAverage1m,
    long NetworkRxBytes,
    long NetworkTxBytes,
    DateTimeOffset LastCapturedAt);
