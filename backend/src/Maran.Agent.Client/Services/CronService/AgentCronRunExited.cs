namespace Maran.Agent.Client.Services.CronService;

/// <summary>The program ended on its own and the agent reported the status it returned.</summary>
/// <param name="ExitCode">The program's exit status; 0 is the status of a successful run.</param>
public sealed record AgentCronRunExited(int ExitCode) : AgentCronRunEnd;
