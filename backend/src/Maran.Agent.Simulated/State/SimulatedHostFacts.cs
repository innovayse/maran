using Maran.Agent.Client.Services.MonitorService;

namespace Maran.Agent.Simulated.State;

/// <summary>
/// What the simulated host is, as opposed to what is hosted on it: its identity, its capacity, the
/// state of its services and the maintenance it is waiting for.
/// </summary>
/// <param name="MachineId">The host's machine id, which a licence may be bound to.</param>
/// <param name="PrimaryInterface">The name of the host's primary network interface.</param>
/// <param name="ClockSync">Whether the host's clock is synchronised.</param>
/// <param name="Reboot">Whether the host is waiting for a reboot.</param>
/// <param name="PendingUpdates">The package updates the host has not applied.</param>
/// <param name="Services">The state of each service the panel watches.</param>
/// <param name="TotalMemoryBytes">Physical memory.</param>
/// <param name="TotalDiskBytes">Size of the filesystem hosted data lives on.</param>
/// <param name="BaseDiskUsedBytes">Disk used by the system itself, before any account's data.</param>
/// <param name="FreeSpaceFloorBytes">The free space below which the agent refuses to start a write.</param>
public sealed record SimulatedHostFacts(
    string MachineId,
    string PrimaryInterface,
    AgentClockSyncState ClockSync,
    AgentRebootRequirement Reboot,
    IReadOnlyList<AgentPendingUpdate> PendingUpdates,
    IReadOnlyList<AgentServiceStatus> Services,
    ulong TotalMemoryBytes,
    ulong TotalDiskBytes,
    ulong BaseDiskUsedBytes,
    ulong FreeSpaceFloorBytes)
{
    /// <summary>Bytes in a gibibyte.</summary>
    private const ulong Gibibyte = 1024UL * 1024 * 1024;

    /// <summary>
    /// A healthy mid-sized host: everything running, the clock synchronised, a handful of updates
    /// waiting and no reboot needed.
    /// </summary>
    public static SimulatedHostFacts Baseline
    {
        get
        {
            return new SimulatedHostFacts(
                "0f3c9a1e5b7d4c2a8e6f1b3d5a7c9e24",
                "eth0",
                AgentClockSyncState.Synchronized,
                AgentRebootRequirement.NotRequired,
                [
                    new AgentPendingUpdate(
                        "openssl", "3.0.13-0ubuntu3.5", "3.0.13-0ubuntu3.4", "noble-security",
                        AgentUpdateClassification.Security),
                    new AgentPendingUpdate(
                        "nginx", "1.24.0-2ubuntu7.1", "1.24.0-2ubuntu7", "noble-security",
                        AgentUpdateClassification.Security),
                    new AgentPendingUpdate(
                        "curl", "8.5.0-2ubuntu10.5", "8.5.0-2ubuntu10.4", "noble-updates",
                        AgentUpdateClassification.Ordinary),
                    new AgentPendingUpdate(
                        "tzdata", "2024b-0ubuntu0.24.04.1", "2024a-2ubuntu1", "noble-updates",
                        AgentUpdateClassification.Ordinary),
                ],
                [
                    new AgentServiceStatus(AgentManagedService.WebServer, AgentServiceState.Running, "active"),
                    new AgentServiceStatus(AgentManagedService.PhpFpm, AgentServiceState.Running, "active"),
                    new AgentServiceStatus(AgentManagedService.Database, AgentServiceState.Running, "active"),
                    new AgentServiceStatus(AgentManagedService.Ftp, AgentServiceState.Running, "active"),
                    new AgentServiceStatus(AgentManagedService.Cron, AgentServiceState.Running, "active"),
                    new AgentServiceStatus(AgentManagedService.Ssh, AgentServiceState.Running, "active"),
                ],
                8 * Gibibyte,
                80 * Gibibyte,
                14 * Gibibyte,
                Gibibyte);
        }
    }
}
