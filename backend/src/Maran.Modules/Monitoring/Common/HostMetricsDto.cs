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
/// <param name="DiskUsedBytes">
/// Bytes the root filesystem accounts as occupied — exactly <c>df</c>'s <c>Used</c>, so the panel
/// never disagrees with the tool an operator checks it against. The root reserve is NOT in it.
/// </param>
/// <param name="DiskReservedBytes">
/// Bytes of the root filesystem that are free but writable by root only: the blocks a filesystem keeps
/// back for the system (5% of ext4 by default, 0 on xfs). <c>df</c> prints no column for it; it is
/// <c>Size - Used - Avail</c>.
/// </param>
/// <param name="DiskAvailableBytes">
/// Bytes a non-root writer — every hosting account — may still add: <c>df</c>'s <c>Avail</c>, and
/// therefore what the server can still hand out.
/// </param>
/// <param name="DiskTotalBytes">
/// Capacity of the root filesystem, <c>df</c>'s <c>Size</c>: always
/// <c>DiskUsedBytes + DiskReservedBytes + DiskAvailableBytes</c>.
/// </param>
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
    long DiskReservedBytes,
    long DiskAvailableBytes,
    long DiskTotalBytes,
    long NetworkRxBytes,
    long NetworkTxBytes,
    double LoadAverage1m,
    double LoadAverage5m,
    double LoadAverage15m);
