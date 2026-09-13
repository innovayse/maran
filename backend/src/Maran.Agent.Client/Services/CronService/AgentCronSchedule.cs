namespace Maran.Agent.Client.Services.CronService;

/// <summary>The standard five-field cron schedule, as the panel carries it.</summary>
/// <param name="Minute">Minute field: <c>0-59</c>, <c>*</c>, a step, a range or a list.</param>
/// <param name="Hour">Hour field: <c>0-23</c> in the same syntax.</param>
/// <param name="DayOfMonth">Day-of-month field: <c>1-31</c> in the same syntax.</param>
/// <param name="Month">Month field: <c>1-12</c> in the same syntax.</param>
/// <param name="DayOfWeek">Day-of-week field: <c>0-6</c> (0 = Sunday) in the same syntax.</param>
/// <remarks>
/// Five separate fields rather than one line, because the agent validates each field against cron's
/// syntax before it writes the crontab, and a single string would have to be split again on the far
/// side to say which half of it was refused.
///
/// Nothing here is validated by this type. The panel's own validator states what a customer may
/// submit and the agent refuses the rest with <c>AgentInvalidInput</c>; a second syntax check here
/// would be a third opinion about cron's grammar, and the one that could silently disagree with the
/// crontab actually written.
/// </remarks>
public sealed record AgentCronSchedule(
    string Minute,
    string Hour,
    string DayOfMonth,
    string Month,
    string DayOfWeek);
