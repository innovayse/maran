using Maran.Agent.Client.Services.MonitorService;
using Maran.Agent.Simulated.State;

namespace Maran.Agent.Simulated.Usage;

/// <summary>
/// The figures a simulated host reports for disk use and machine load: functions of a name and a
/// moment only, so the same account shows the same usage and the same minute shows the same load
/// every time the demonstration is looked at.
/// </summary>
/// <remarks>
/// There is no randomness anywhere in here and no ambient clock; the moment is always passed in from
/// the injected clock. The curves are plausible rather than meaningful: usage grows slowly from a
/// per-account base, load follows a slow wave. Nothing is measured, because nothing is running.
/// </remarks>
public static class SimulatedUsageCurve
{
    /// <summary>Bytes in a mebibyte.</summary>
    private const ulong Mebibyte = 1024UL * 1024;

    /// <summary>The disk an account uses at <paramref name="now"/>.</summary>
    /// <param name="account">The account.</param>
    /// <param name="now">The moment to report for.</param>
    /// <returns>
    /// A pinned figure verbatim; otherwise a base of 8 to 200 MiB that depends on the name, growing
    /// from the account's creation at 64 KiB to 1 MiB a day, and never above a quota that is set,
    /// because a quota stops writes.
    /// </returns>
    public static ulong AccountUsedBytes(SimulatedAccount account, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (account.PinnedUsedBytes is { } pinned)
        {
            return pinned;
        }

        var hash = StableHash.Of(account.Username);
        var baseBytes = (8 * Mebibyte) + (hash % (192 * Mebibyte));
        var growthPerDay = (64UL * 1024) + ((hash >> 20) % (960UL * 1024));
        var days = Math.Max(0.0, (now - account.CreatedAt).TotalDays);
        var used = baseBytes + (ulong)(days * growthPerDay);

        return account.QuotaBytes > 0 ? Math.Min(used, account.QuotaBytes) : used;
    }

    /// <summary>The host's load and capacity figures at <paramref name="now"/>.</summary>
    /// <param name="facts">What the host is: capacity and system disk use.</param>
    /// <param name="accountsUsedBytes">The disk the hosted accounts use together.</param>
    /// <param name="now">The moment to report for.</param>
    /// <returns>
    /// A CPU figure between 8 and 20 percent on a slow wave, memory at 30 to 35 percent of the total,
    /// disk as the system's use plus the accounts' with 5 percent reserved, and network counters that
    /// only ever grow.
    /// </returns>
    public static AgentHostMetrics HostMetrics(SimulatedHostFacts facts, ulong accountsUsedBytes, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var seconds = now.ToUnixTimeSeconds();
        var phase = seconds / 60.0 * 0.35;
        var wave = (1.0 + Math.Sin(phase)) / 2.0;
        var cpu = 8.0 + (12.0 * wave);
        var memoryUsed = (ulong)(facts.TotalMemoryBytes * (0.30 + (0.05 * ((1.0 + Math.Sin(phase / 2.0)) / 2.0))));
        var diskUsed = Math.Min(facts.TotalDiskBytes, facts.BaseDiskUsedBytes + accountsUsedBytes);
        var diskReserved = facts.TotalDiskBytes / 20;
        var diskAvailable = facts.TotalDiskBytes - diskUsed > diskReserved
            ? facts.TotalDiskBytes - diskUsed - diskReserved
            : 0UL;
        var load = cpu / 100.0 * 2.0;

        return new AgentHostMetrics(
            cpu,
            memoryUsed,
            facts.TotalMemoryBytes,
            diskUsed,
            diskReserved,
            diskAvailable,
            facts.TotalDiskBytes,
            (ulong)Math.Max(0, seconds) * 8192UL,
            (ulong)Math.Max(0, seconds) * 3072UL,
            load,
            load * 0.9,
            load * 0.8,
            facts.FreeSpaceFloorBytes);
    }
}
