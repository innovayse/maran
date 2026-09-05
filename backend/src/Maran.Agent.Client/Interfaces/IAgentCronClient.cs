using Maran.Agent.Client.Services.CronService;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// The panel's view of an account's scheduled tasks. Deciding which entries an account may own, and
/// how many, is the panel's job; this is only the installation, enumeration and removal of them in
/// that account's own crontab.
/// </summary>
/// <remarks>
/// Every call names an account, and the agent installs the entry in that account's user crontab, so
/// the command later runs under the account's own uid via the system cron daemon. The agent never
/// executes it, which is why this is not a hole in the rule that the panel runs no caller-supplied
/// program (rules/architecture.md): the account already has the same reach over SFTP.
///
/// The account owns the crontab and can edit it directly, so nothing read back here is evidence of
/// what the panel installed. It is what the server currently holds.
/// </remarks>
public interface IAgentCronClient
{
    /// <summary>Lists every cron entry currently installed for an account.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// The entries in the order the agent sent them, or a typed failure. The rows carry no exit
    /// status and no last-run time: a listing does not read them, and
    /// <see cref="GetEntryOutputAsync"/> is where that question is answered.
    /// </returns>
    Task<Result<IReadOnlyList<AgentCronEntry>>> ListEntriesAsync(
        string accountUsername,
        CancellationToken cancellationToken);

    /// <summary>Appends a new cron entry to an account's crontab.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="schedule">When the entry is to run.</param>
    /// <param name="command">The command line to install, verbatim.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// The agent's identifier for the created entry, or a typed failure — <c>AgentAlreadyExists</c>
    /// when an entry with the same schedule and command is already installed, which the agent
    /// answers rather than duplicating it.
    /// </returns>
    Task<Result<string>> CreateEntryAsync(
        string accountUsername,
        AgentCronSchedule schedule,
        string command,
        CancellationToken cancellationToken);

    /// <summary>Replaces the schedule and the command of an existing entry.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="entryId">Identifier of the entry to rewrite, from a listing or a creation.</param>
    /// <param name="schedule">The new schedule.</param>
    /// <param name="command">The new command line, verbatim.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure — <c>AgentNotFound</c> for an entry that is not there.</returns>
    /// <remarks>
    /// Rewrites what the entry runs and leaves its enablement exactly as it was;
    /// <see cref="SetEntryEnabledAsync"/> is the only way that changes. The two are separate calls
    /// on purpose, because an update that also carried enablement would silently switch a disabled
    /// entry back on whenever a caller edited its command without thinking about the flag.
    /// </remarks>
    Task<Result<bool>> UpdateEntryAsync(
        string accountUsername,
        string entryId,
        AgentCronSchedule schedule,
        string command,
        CancellationToken cancellationToken);

    /// <summary>Removes a cron entry from an account's crontab.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="entryId">Identifier of the entry to remove.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure — <c>AgentNotFound</c> for an entry that is not there.</returns>
    Task<Result<bool>> DeleteEntryAsync(
        string accountUsername,
        string entryId,
        CancellationToken cancellationToken);

    /// <summary>Enables or disables an entry without touching its schedule or its command.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="entryId">Identifier of the entry to switch.</param>
    /// <param name="enabled">True installs it as a live crontab line; false comments it out.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure — <c>AgentNotFound</c> for an entry that is not there.</returns>
    /// <remarks>Disabling keeps the entry in the crontab, so switching one off never loses it.</remarks>
    Task<Result<bool>> SetEntryEnabledAsync(
        string accountUsername,
        string entryId,
        bool enabled,
        CancellationToken cancellationToken);

    /// <summary>Reads what an entry's most recent run left behind.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="entryId">Identifier of the entry to read.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// What the last run left, or NULL when the agent reported no trace of a run at all, or a typed
    /// failure. Null is not an empty run: an entry that ran and printed nothing returns a value
    /// whose <c>Output</c> is the empty string, and a caller that showed "never run" for it would be
    /// telling a customer their job is not firing.
    /// </returns>
    /// <remarks>
    /// Null reads as "has never run", and that is an inference rather than a fact the agent states.
    /// No trace is also what an entry whose traces are unreadable looks like — a status file that
    /// will not parse, a modification time before the epoch — so a run whose evidence was deleted
    /// or corrupted arrives here the same way. The panel's own record of when it installed the
    /// entry is the second opinion worth consulting before telling a customer their job has never
    /// fired.
    /// </remarks>
    Task<Result<AgentCronRunOutput?>> GetEntryOutputAsync(
        string accountUsername,
        string entryId,
        CancellationToken cancellationToken);

    /// <summary>Reads the environment assignments the agent manages in an account's crontab.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// The managed assignments in the order the crontab holds them, or a typed failure. Assignments
    /// the account or the host wrote outside the agent's own region are not reported.
    /// </returns>
    Task<Result<IReadOnlyList<AgentCronEnvVar>>> GetEnvironmentAsync(
        string accountUsername,
        CancellationToken cancellationToken);

    /// <summary>Replaces the agent-managed environment assignments, whole.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="variables">
    /// The complete new set. It REPLACES the managed assignments rather than merging into them, so a
    /// name absent from this list is removed — and an empty list is how every managed assignment is
    /// cleared, which is a request the agent honours rather than an error.
    /// </param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure.</returns>
    Task<Result<bool>> SetEnvironmentAsync(
        string accountUsername,
        IReadOnlyList<AgentCronEnvVar> variables,
        CancellationToken cancellationToken);
}
