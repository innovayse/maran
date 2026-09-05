namespace Maran.Agent.Client.Services.CronService;

/// <summary>What a cron entry's most recent run left behind.</summary>
/// <param name="Output">
/// The tail of the run's output, bounded by the agent and decoded lossily, or null when the agent
/// reported none. Null and an empty string are different answers: empty means the run said nothing,
/// which is the ordinary outcome of a healthy job.
/// </param>
/// <param name="LastExitCode">
/// The exit status of the most recent run, or null when the agent reported none. Nullable rather
/// than a sentinel because 0 is the status of a successful run and cannot also mean "not reported".
/// </param>
/// <param name="LastRunAtUnix">
/// When the most recent run finished, in Unix seconds (UTC), or null when the agent reported none.
/// It is the modification time of the file the run's status was written to — the time the RUN ended,
/// not a clock reading taken by the call that asked.
/// </param>
/// <remarks>
/// Both the output and the status file live under the account's home and the account can write
/// them, so everything here is what the ACCOUNT's own runs left behind. It is informational, and it
/// is never evidence of what the agent did.
///
/// Kept as Unix seconds rather than converted to a <see cref="System.DateTimeOffset"/>: the panel
/// renders times in the request's culture and time zone, and a conversion here would fix an
/// interpretation in the layer furthest from the reader. The panel's own clock abstraction owns
/// that (rules/csharp.md, no ambient clock).
/// </remarks>
public sealed record AgentCronRunOutput(string? Output, int? LastExitCode, long? LastRunAtUnix);
