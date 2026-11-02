using Maran.Modules.Cron.Domain.Enums;

namespace Maran.Modules.Cron.Domain.ValueObjects;

/// <summary>One of the agent's ceilings stopped the program before it ended, so the run has no status.</summary>
/// <param name="Limit">The ceiling that stopped it.</param>
public sealed record CronRunStopped(CronRunLimit Limit) : CronRunEnd;
