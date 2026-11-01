namespace Maran.Agent.Client.Services.CronService;

/// <summary>What one cron entry run by hand reported (issue #158).</summary>
/// <param name="DurationMilliseconds">How long the run took, measured by the agent.</param>
/// <param name="ExitCode">
/// The program's exit status, or null when a ceiling stopped it or it ended with no status of its own.
/// Nullable rather than a sentinel, because 0 is the status of a successful run.
/// </param>
/// <param name="Stdout">What the program wrote to standard output, at most 64 KiB, decoded lossily.</param>
/// <param name="Stderr">What the program wrote to standard error, at most 64 KiB, decoded lossily.</param>
/// <param name="Limit">The ceiling that stopped the run, or null when the program ended on its own.</param>
/// <remarks>
/// Everything here is what the CUSTOMER's program printed, as the account. It goes back to its owner
/// and never into a log line or the audit journal (<c>CronAuditJournal</c>).
/// </remarks>
public sealed record AgentCronRun(
    long DurationMilliseconds,
    int? ExitCode,
    string Stdout,
    string Stderr,
    AgentCronRunLimit? Limit);
