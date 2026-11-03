namespace Maran.Modules.Cron.Domain.Enums;

/// <summary>
/// How often the schedule builder's pattern repeats: the closed set of shapes a form can express
/// without becoming a worse version of the five crontab fields themselves.
/// </summary>
/// <remarks>
/// Reference data, not stored state: the crontab keeps five fields and never a frequency, so this
/// enum exists only to give the builder's choices one authoritative, localized source (the panel's
/// <c>GetCronOptions</c> answer). Anything outside these five is what a raw expression is for.
/// </remarks>
public enum CronFrequency
{
    /// <summary>Every minute of every hour.</summary>
    EveryMinute = 0,

    /// <summary>Once an hour, at a chosen minute.</summary>
    Hourly = 1,

    /// <summary>Once a day, at a chosen hour and minute.</summary>
    Daily = 2,

    /// <summary>Once a week, on a chosen weekday, hour and minute.</summary>
    Weekly = 3,

    /// <summary>Once a month, on a chosen day of the month, hour and minute.</summary>
    Monthly = 4,
}
