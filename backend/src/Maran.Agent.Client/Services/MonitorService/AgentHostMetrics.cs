namespace Maran.Agent.Client.Services.MonitorService;

/// <summary>A point-in-time snapshot of the host's resource use.</summary>
/// <param name="CpuPercent">CPU utilisation across all cores, 0.0-100.0.</param>
/// <param name="MemoryUsedBytes">Memory currently in use.</param>
/// <param name="MemoryTotalBytes">Total installed memory.</param>
/// <param name="DiskUsedBytes">Disk space in use on the root filesystem.</param>
/// <param name="DiskTotalBytes">Total capacity of the root filesystem.</param>
/// <param name="NetworkRxBytes">
/// Bytes received since the host booted, summed over the physical interfaces with loopback
/// excluded. A COUNTER, not a per-interval figure.
/// </param>
/// <param name="NetworkTxBytes">Bytes sent since the host booted, on the same terms.</param>
/// <param name="LoadAverage1m">System load average over the last minute.</param>
/// <param name="LoadAverage5m">System load average over the last five minutes.</param>
/// <param name="LoadAverage15m">System load average over the last fifteen minutes.</param>
/// <remarks>
/// The two network figures are the only ones that are not a level, and the difference matters to
/// whoever renders them: the agent holds no previous sample and takes one reading, so a RATE is
/// derived by dividing the difference between two readings by the seconds that actually elapsed
/// between them — never by assuming the polling interval. A negative difference is a reboot or an
/// interface that went away, and clamps to zero rather than rendering as a spike.
/// </remarks>
public sealed record AgentHostMetrics(
    double CpuPercent,
    ulong MemoryUsedBytes,
    ulong MemoryTotalBytes,
    ulong DiskUsedBytes,
    ulong DiskTotalBytes,
    ulong NetworkRxBytes,
    ulong NetworkTxBytes,
    double LoadAverage1m,
    double LoadAverage5m,
    double LoadAverage15m);
