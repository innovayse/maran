namespace Maran.Modules.Cron.Domain.Enums;

/// <summary>
/// The agent's ceiling that stopped a run started by hand. A run that reached none holds no value of
/// this type (<see cref="Entities.CronRun.Limit"/> is null), so "no ceiling" can never sit beside an
/// exit status that contradicts it.
/// </summary>
public enum CronRunLimit
{
    /// <summary>The run lasted as long as the agent's runtime ceiling and was stopped.</summary>
    Runtime = 1,

    /// <summary>An output stream went past the agent's ceiling; the run was stopped and no output kept.</summary>
    Output = 2,
}
