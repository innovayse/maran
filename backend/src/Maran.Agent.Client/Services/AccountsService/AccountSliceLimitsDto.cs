namespace Maran.Agent.Client.Services.AccountsService;

/// <summary>
/// The CPU, memory and process ceilings one account's systemd slice is held to, sent together because
/// the agent replaces all three at once.
/// </summary>
/// <param name="CpuQuotaPercent">The share of CPU time, in percent of ONE CPU (200 is two whole cores).</param>
/// <param name="MemoryMaxMib">The memory ceiling, in MiB.</param>
/// <param name="TasksMax">The ceiling on processes and threads.</param>
public sealed record AccountSliceLimitsDto(SliceCeiling CpuQuotaPercent, SliceCeiling MemoryMaxMib, SliceCeiling TasksMax);
