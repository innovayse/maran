namespace Maran.Modules.Cron.Common;

/// <summary>
/// The program ended on its own but reported no status of its own, because it died of a signal
/// (<c>kind</c> is <c>withoutStatus</c>).
/// </summary>
public sealed record CronRunWithoutStatusDto : CronRunEndDto;
