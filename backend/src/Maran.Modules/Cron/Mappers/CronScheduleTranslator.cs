using Maran.Agent.Client.Services.CronService;
using Maran.Modules.Cron.Common;

namespace Maran.Modules.Cron.Mappers;

/// <summary>
/// Converts between the panel's <see cref="CronScheduleDto"/> and the agent client's
/// <see cref="AgentCronSchedule"/>.
/// </summary>
/// <remarks>
/// Five fields carried across a boundary, in one place rather than at each of the three call sites
/// that need it. The conversion is trivial and that is exactly the danger: two of the five fields
/// are one-based and three are not, all five are strings, and a pair transposed in one copy would
/// compile, pass every type check, and silently install a schedule that runs at another time. One
/// copy is one thing to get right.
/// </remarks>
public static class CronScheduleTranslator
{
    /// <summary>Shapes a schedule for the agent client.</summary>
    /// <param name="schedule">The validated schedule the caller sent.</param>
    /// <returns>The same five fields, unaltered.</returns>
    public static AgentCronSchedule ToAgentSchedule(CronScheduleDto schedule)
    {
        return new AgentCronSchedule(
            schedule.Minute,
            schedule.Hour,
            schedule.DayOfMonth,
            schedule.Month,
            schedule.DayOfWeek);
    }

    /// <summary>Shapes a schedule the agent reported for the panel's own responses.</summary>
    /// <param name="schedule">The schedule as the agent read it out of the crontab.</param>
    /// <returns>The same five fields, unaltered.</returns>
    public static CronScheduleDto ToDto(AgentCronSchedule schedule)
    {
        return new CronScheduleDto(
            schedule.Minute,
            schedule.Hour,
            schedule.DayOfMonth,
            schedule.Month,
            schedule.DayOfWeek);
    }
}
