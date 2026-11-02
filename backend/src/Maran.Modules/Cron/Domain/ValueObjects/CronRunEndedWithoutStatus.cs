namespace Maran.Modules.Cron.Domain.ValueObjects;

/// <summary>
/// The program ended on its own but reported no status of its own (it died of a signal). It is a
/// failure: only a 0 the program itself returned says it worked.
/// </summary>
public sealed record CronRunEndedWithoutStatus : CronRunEnd;
