namespace Maran.Agent.Client.Services.CronService;

/// <summary>One of the agent's ceilings stopped the run, so it has no status of its own.</summary>
/// <param name="Limit">The ceiling that stopped it.</param>
public sealed record AgentCronRunStopped(AgentCronRunLimit Limit) : AgentCronRunEnd;
