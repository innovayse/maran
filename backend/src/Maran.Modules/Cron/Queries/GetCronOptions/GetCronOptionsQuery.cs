namespace Maran.Modules.Cron.Queries.GetCronOptions;

/// <summary>
/// Reads the closed sets the scheduled-tasks screens select from: the schedule builder's
/// frequencies, weekdays, hours, minutes and days of the month, and the run outcomes the history
/// filter offers.
/// </summary>
/// <remarks>
/// Reference data (rules/architecture.md "The backend owns the data, the SPA renders it"): the same
/// for every caller, so it carries no tenant and discloses nothing about any account. The validators
/// remain the boundary — a choice list is advice.
/// </remarks>
public sealed record GetCronOptionsQuery;
