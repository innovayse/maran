namespace Maran.Agent.Client.Services.MonitorService;

/// <summary>A point-in-time snapshot of the host's resource use.</summary>
/// <param name="CpuPercent">CPU utilisation across all cores, 0.0-100.0.</param>
/// <param name="MemoryUsedBytes">Memory currently in use.</param>
/// <param name="MemoryTotalBytes">Total installed memory.</param>
/// <param name="DiskUsedBytes">
/// Bytes the root filesystem accounts as occupied — <c>df</c>'s <c>Used</c>, the reserve excluded.
/// </param>
/// <param name="DiskReservedBytes">
/// Bytes of the root filesystem free but writable by root only (5% of ext4 by default, 0 on xfs).
/// </param>
/// <param name="DiskAvailableBytes">
/// Bytes any non-root writer, every hosting account included, may still add — <c>df</c>'s <c>Avail</c>.
/// </param>
/// <param name="DiskTotalBytes">
/// Total capacity of the root filesystem — <c>df</c>'s <c>Size</c>, which the agent states is the sum
/// of the three figures above.
/// </param>
/// <param name="NetworkRxBytes">
/// Bytes received since the host booted, summed over the physical interfaces with loopback
/// excluded. A COUNTER, not a per-interval figure.
/// </param>
/// <param name="NetworkTxBytes">Bytes sent since the host booted, on the same terms.</param>
/// <param name="LoadAverage1m">System load average over the last minute.</param>
/// <param name="LoadAverage5m">System load average over the last five minutes.</param>
/// <param name="LoadAverage15m">System load average over the last fifteen minutes.</param>
/// <param name="FreeSpaceFloorBytes">
/// The agent's free-space floor: the least room a filesystem must still have before the agent starts
/// an account creation, a backup, a restore or an upload that writes into it (issue #148). Set by the
/// agent's own command line; at least one mebibyte from a revision-7 agent.
/// </param>
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
    ulong DiskReservedBytes,
    ulong DiskAvailableBytes,
    ulong DiskTotalBytes,
    ulong NetworkRxBytes,
    ulong NetworkTxBytes,
    double LoadAverage1m,
    double LoadAverage5m,
    double LoadAverage15m,
    ulong FreeSpaceFloorBytes);
