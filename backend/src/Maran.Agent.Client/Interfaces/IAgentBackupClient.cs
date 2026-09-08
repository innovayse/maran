using Maran.Agent.Client.Services.BackupService;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// The panel's view of an account's backups: creating one, restoring from one, listing what exists,
/// deleting an artifact, and asking whether a destination would serve those artifacts to anyone.
/// </summary>
/// <remarks>
/// Every call names its destination, because the agent holds no destination configuration of its
/// own. The agent that ships today reaches only
/// <see cref="AgentBackupDestinationKind.Local"/>: a destination naming a bucket is refused at the
/// agent's service boundary with a not-implemented failure, which arrives here as the
/// <c>AgentNotImplemented</c> code and must be shown as itself. It is never a generic failure, and
/// there is no fallback to a local destination anywhere in this client — a backup silently written
/// somewhere other than where the operator asked is worse than a refusal.
/// </remarks>
public interface IAgentBackupClient
{
    /// <summary>Creates a backup of an account: its home directory, plus a dump of each of its databases.</summary>
    /// <param name="accountUsername">System username of the account being backed up.</param>
    /// <param name="backupId">
    /// Caller-assigned identifier for this backup, used for idempotency and to target a later restore
    /// or delete. Re-creating a backup id that already completed is answered immediately rather than
    /// redone.
    /// </param>
    /// <param name="destination">Where to store the resulting artifact.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>
    /// Progress events followed by exactly one terminal event naming the outcome — including the case
    /// where the stream ended without the agent reporting one, and the case where the caller
    /// cancelled, neither of which is a success.
    /// </returns>
    IAsyncEnumerable<BackupCreateEvent> CreateAsync(
        string accountUsername,
        string backupId,
        AgentBackupDestination destination,
        CancellationToken cancellationToken);

    /// <summary>Restores an account from a backup, overwriting its files and reloading its databases.</summary>
    /// <param name="accountUsername">System username of the account being restored into.</param>
    /// <param name="backupId">Identifier of the backup to restore.</param>
    /// <param name="destination">Where the artifact currently resides.</param>
    /// <param name="expectedSha256">
    /// The digest the panel recorded when the artifact was created, hex and lowercase. The agent
    /// hashes the artifact and refuses the restore when the two differ, before it drops a single
    /// database. An empty value is refused by the agent rather than treated as "do not check".
    /// </param>
    /// <param name="allowedDatabases">
    /// Every database the panel still knows this account owns. The agent restores the intersection of
    /// this list and the archive's manifest and refuses a manifest naming anything outside it, so an
    /// empty list restores no database at all.
    /// </param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>
    /// Progress events followed by exactly one terminal event. The terminal event's kind says whether
    /// the agent reported an outcome; the outcome carried on it says how much of the restore
    /// happened, and the two are not the same question.
    /// </returns>
    IAsyncEnumerable<BackupRestoreEvent> RestoreAsync(
        string accountUsername,
        string backupId,
        AgentBackupDestination destination,
        string expectedSha256,
        IReadOnlyList<string> allowedDatabases,
        CancellationToken cancellationToken);

    /// <summary>Lists the backups the agent knows of for an account.</summary>
    /// <param name="accountUsername">System username of the account whose backups are listed.</param>
    /// <param name="destination">The destination the listing is about.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// Every artifact that exists, described where it could be described and carrying the reason where
    /// it could not — an entry retention must still be able to see. A listing carrying an entry that
    /// claims to be describable and carries no manifest is refused as a whole rather than partially
    /// believed.
    /// </returns>
    Task<Result<IReadOnlyList<AgentBackupSummary>>> ListAsync(
        string accountUsername,
        AgentBackupDestination destination,
        CancellationToken cancellationToken);

    /// <summary>Deletes a backup's stored artifact.</summary>
    /// <param name="accountUsername">System username of the account owning the backup.</param>
    /// <param name="backupId">Identifier of the backup to delete.</param>
    /// <param name="destination">Where the artifact currently resides.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure. Deleting an artifact that is not there is a not-found failure.</returns>
    Task<Result<bool>> DeleteAsync(
        string accountUsername,
        string backupId,
        AgentBackupDestination destination,
        CancellationToken cancellationToken);

    /// <summary>Asks whether a destination would serve this product's archives to anyone.</summary>
    /// <param name="destination">The destination to probe, credentials included.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// The verdict, or a typed failure. The agent that ships today performs no probe at all and
    /// answers <c>AgentNotImplemented</c> for every destination, which the caller must treat as "not
    /// established" — never as permission to save the destination. Only
    /// <see cref="AgentPublicReadVerdict.Private"/> may do that.
    /// </returns>
    Task<Result<AgentPublicReadVerdict>> ProbeAsync(
        AgentBackupDestination destination,
        CancellationToken cancellationToken);
}
