namespace Maran.Modules.Cron.Common;

/// <summary>
/// Every closed set the scheduled-tasks screens select from, produced by the panel so the SPA holds
/// no domain constants and no translated vocabulary of its own.
/// </summary>
/// <param name="Frequencies">The shapes the schedule builder offers, in reading order.</param>
/// <param name="Weekdays">The seven weekdays; the value is the number cron uses, Sunday being 0.</param>
/// <param name="Hours">The hours of the day, <c>0</c> to <c>23</c>.</param>
/// <param name="Minutes">The minutes of the hour, <c>0</c> to <c>59</c>.</param>
/// <param name="DaysOfMonth">The days of the month, <c>1</c> to <c>31</c>.</param>
/// <param name="RunOutcomes">The outcomes a run started by hand can end in, for the history filter.</param>
public sealed record CronOptionsDto(
    IReadOnlyList<CronChoiceDto> Frequencies,
    IReadOnlyList<CronChoiceDto> Weekdays,
    IReadOnlyList<CronChoiceDto> Hours,
    IReadOnlyList<CronChoiceDto> Minutes,
    IReadOnlyList<CronChoiceDto> DaysOfMonth,
    IReadOnlyList<CronChoiceDto> RunOutcomes);
