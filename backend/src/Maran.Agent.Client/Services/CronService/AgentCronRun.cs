namespace Maran.Agent.Client.Services.CronService;

/// <summary>What one cron entry run by hand reported (issue #158).</summary>
/// <param name="DurationMilliseconds">How long the run took, measured by the agent.</param>
/// <param name="End">How the run ended: with a status, with none, or stopped by one of the agent's ceilings.</param>
/// <param name="Stdout">What the program wrote to standard output, at most 64 KiB, decoded lossily.</param>
/// <param name="Stderr">What the program wrote to standard error, at most 64 KiB, decoded lossily.</param>
/// <remarks>
/// Everything here is what the CUSTOMER's program printed, as the account. It goes back to its owner
/// and never into a log line or the audit journal (<c>CronAuditJournal</c>).
/// </remarks>
public sealed record AgentCronRun(
    long DurationMilliseconds,
    AgentCronRunEnd End,
    string Stdout,
    string Stderr);
