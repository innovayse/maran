namespace Maran.Modules.Cron.Common;

/// <summary>One entry of a choice list the scheduled-tasks screens select from.</summary>
/// <param name="Value">
/// The machine-stable value, which is what the screen sends back: a frequency name, a cron weekday
/// number, an hour, a minute, a day of the month or a run outcome. The validators accept exactly
/// the values these lists offer (and, for the schedule fields, more — ranges, steps and lists in raw
/// mode), so the list is advice and the endpoint remains the boundary.
/// </param>
/// <param name="Label">The choice as an operator reads it, resolved for the request's culture.</param>
public sealed record CronChoiceDto(string Value, string Label);
