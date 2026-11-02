namespace Maran.Agent.Client.Services.CronService;

/// <summary>
/// The program ended on its own but reported no status of its own (it died of a signal). A killed run
/// has no status, and a 0 invented for it would read as a success.
/// </summary>
public sealed record AgentCronRunWithoutStatus : AgentCronRunEnd;
