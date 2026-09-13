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

    /// <summary>Appends a new cron entry to an account's crontab, within a stated allowance.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="schedule">When the entry is to run.</param>
    /// <param name="command">The command line to install, verbatim.</param>
    /// <param name="maxEntries">
    /// How many managed entries the account's plan allows in total, or <c>null</c> to state no
    /// allowance. When stated and the crontab already holds at least this many, the agent installs
    /// nothing and answers <c>AgentLimitReached</c>.
    /// </param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// The agent's identifier for the created entry, or a typed failure — <c>AgentAlreadyExists</c>
    /// when an entry with the same schedule and command is already installed, which the agent
    /// answers rather than duplicating it, and <c>AgentLimitReached</c> when
    /// <paramref name="maxEntries"/> is stated and already met.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>The allowance is sent rather than only checked here, and cron is the one call in this
    /// client that needs that.</b> Every other countable plan limit in the panel is made atomic by
    /// counting rows and inserting inside one database transaction. This module keeps no rows: the
    /// count lives in the crontab on the host, so the panel's only way to take it is
    /// <see cref="ListEntriesAsync"/> — a second call — and two requests interleaving between that
    /// call and this one both install. Inside the agent the two are already one act: the operation
    /// takes the account's cron lock and then reads the whole crontab, so the count it takes cannot
    /// be stale by the time it installs. Sending the number is what lets the comparison happen
    /// there.
    /// </para>
    /// <para>
    /// This does not move the policy to the agent, and a caller must not read it that way. The agent
    /// holds no plan and invents no limit; it compares the number this call carried. Deciding what
    /// the account is allowed, and refusing a full plan before the host is touched at all, stay
    /// where <c>rules/security.md</c> puts them — in the panel.
    /// </para>
    /// <para>
    /// <c>null</c> means no allowance is stated and the agent enforces none, which is the behaviour
    /// this call had before the parameter existed. It is nullable rather than a zero sentinel because
    /// zero is a real allowance — a plan permitting no scheduled tasks at all — and because a bare
    /// count would arrive at an agent as proto3's default of 0 from any caller that did not set it,
    /// which as an allowance means "allow nothing".
    /// </para>
    /// </remarks>
    Task<Result<string>> CreateEntryAsync(
        string accountUsername,
        AgentCronSchedule schedule,
        string command,
        uint? maxEntries,
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

    /// <summary>Suppresses, or restores, every managed entry of one account at once.</summary>
    /// <param name="accountUsername">System username of the account whose whole crontab is affected.</param>
    /// <param name="suspended"><c>true</c> to suppress every managed entry, <c>false</c> to restore them.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure. An account with no crontab is a success.</returns>
    /// <remarks>
    /// <para>
    /// Its own call and NOT a loop over <see cref="SetEntryEnabledAsync"/>, which is the point of it
    /// existing: <c>enabled</c> is the CUSTOMER's switch. Driving a suspension through it would make
    /// the resume switch back on every entry the customer had turned off themselves, and the panel
    /// keeps no cron rows to put them back from — the crontab on the host is the only record of that
    /// choice there is. The agent therefore carries a second, orthogonal marker.
    /// </para>
    /// <para>
    /// A suspended entry keeps its id, its command, its schedule and its own enablement, and is
    /// merely invisible to cron. Idempotent in both directions. FOREIGN crontab lines are untouched
    /// and keep firing; the suspension state reports how many there are.
    /// </para>
    /// </remarks>
    Task<Result<bool>> SetAccountSuspendedAsync(
        string accountUsername,
        bool suspended,
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
