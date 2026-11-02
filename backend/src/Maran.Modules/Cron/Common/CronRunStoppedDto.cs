using Maran.Modules.Cron.Domain.Enums;

namespace Maran.Modules.Cron.Common;

/// <summary>One of the agent's ceilings stopped the run, so it has no status (<c>kind</c> is <c>stopped</c>).</summary>
/// <param name="Limit">The ceiling that stopped it.</param>
public sealed record CronRunStoppedDto(CronRunLimit Limit) : CronRunEndDto;
