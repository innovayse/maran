namespace Maran.Modules.Cron.Domain.Enums;

/// <summary>How a run started by hand ended, as the history shows it and filters on it.</summary>
public enum CronRunOutcome
{
    /// <summary>The program ended on its own with status 0.</summary>
    Succeeded = 1,

    /// <summary>The program ended on its own with any other status, or reported none.</summary>
    Failed = 2,

    /// <summary>One of the agent's ceilings stopped the program before it ended.</summary>
    Stopped = 3,
}
