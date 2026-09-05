namespace Maran.Modules.Cron.Common;

/// <summary>The five time fields of one crontab line, as the panel carries them in and out.</summary>
/// <remarks>
/// Five separate fields rather than one line, matching the agent's own contract: each field is
/// validated on its own, so a refusal can say which one was wrong, and a space cannot smuggle a
/// sixth field past a check meant for five.
///
/// One type for both directions — a creation, an update and a listing all describe a schedule the
/// same way — because there is no field a customer may send that the panel would not show back.
/// </remarks>
/// <param name="Minute">Minute field: <c>0-59</c>, <c>*</c>, a step, a range or a list.</param>
/// <param name="Hour">Hour field: <c>0-23</c> in the same syntax.</param>
/// <param name="DayOfMonth">Day-of-month field: <c>1-31</c> in the same syntax.</param>
/// <param name="Month">Month field: <c>1-12</c> in the same syntax.</param>
/// <param name="DayOfWeek">Day-of-week field: <c>0-6</c> (0 = Sunday) in the same syntax.</param>
public sealed record CronScheduleDto(
    string Minute,
    string Hour,
    string DayOfMonth,
    string Month,
    string DayOfWeek);
