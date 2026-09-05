namespace Maran.Modules.Monitoring.Common;

/// <summary>The host's resource use right now, as the dashboard shows it.</summary>
/// <remarks>
/// A LIVE reading, taken by asking the agent, not the newest row in <c>monitoring.Samples</c>. The
/// two answer different questions: this one is "what is the machine doing", the table is "what has
/// it been doing", and a dashboard fed from the table would lag by up to a full sampling interval
/// and would show nothing at all on a panel whose sampler has not yet run.
///
/// The two network figures are the counters the agent reports, passed through unaltered. A rate
/// cannot be derived from one reading — see <c>NetworkRateCalculator</c>, which needs two — so this
/// type deliberately does not offer one rather than offering a fabricated zero.
/// </remarks>
/// <param name="CpuPercent">Processor utilisation across all cores, 0.0-100.0.</param>
/// <param name="MemoryUsedBytes">Memory in use, in bytes.</param>
/// <param name="MemoryTotalBytes">Total installed memory, in bytes.</param>
/// <param name="DiskUsedBytes">Disk space in use on the root filesystem, in bytes.</param>
/// <param name="DiskTotalBytes">Total capacity of the root filesystem, in bytes.</param>
/// <param name="NetworkRxBytes">Bytes received since the host booted — a counter, not traffic.</param>
/// <param name="NetworkTxBytes">Bytes sent since the host booted — a counter, not traffic.</param>
/// <param name="LoadAverage1m">System load average over the last minute.</param>
/// <param name="LoadAverage5m">System load average over the last five minutes.</param>
/// <param name="LoadAverage15m">System load average over the last fifteen minutes.</param>
public sealed record HostMetricsDto(
    double CpuPercent,
    long MemoryUsedBytes,
    long MemoryTotalBytes,
    long DiskUsedBytes,
    long DiskTotalBytes,
    long NetworkRxBytes,
    long NetworkTxBytes,
    double LoadAverage1m,
    double LoadAverage5m,
    double LoadAverage15m);
