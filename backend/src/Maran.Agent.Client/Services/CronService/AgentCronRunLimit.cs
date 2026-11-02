namespace Maran.Agent.Client.Services.CronService;

/// <summary>
/// The ceiling that stopped a cron entry run by hand. A run that reached none carries no value of this
/// type at all (<see cref="AgentCronRun.Limit"/> is null), so "no ceiling" is never a member that could
/// sit beside an exit status it contradicts.
/// </summary>
public enum AgentCronRunLimit
{
    /// <summary>The run lasted as long as the agent's runtime ceiling and was stopped.</summary>
    Runtime,

    /// <summary>An output stream went past the agent's ceiling; the run was stopped and no output kept.</summary>
    Output,
}
