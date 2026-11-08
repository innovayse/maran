namespace Maran.Modules.Monitoring.Models;

/// <summary>
/// How busy the processor and the memory of the server were in one round, for the alert observer
/// (issue #142).
/// </summary>
/// <param name="CpuPercent">Processor utilisation across all cores, 0.0-100.0.</param>
/// <param name="MemoryUsedPercent">
/// Memory in use (total minus the kernel's available figure) as a percentage of the total, or
/// <c>null</c> when the agent reported a total of zero and no percentage can be computed.
/// </param>
public sealed record HostLoadReading(double CpuPercent, double? MemoryUsedPercent);
