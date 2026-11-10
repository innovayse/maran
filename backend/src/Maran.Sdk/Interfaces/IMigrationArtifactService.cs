using Maran.Sdk.Contracts;

namespace Maran.Sdk.Interfaces;

/// <summary>
/// The act-window onto the Backups module that a move needs: make a backup to be exported, and restore one
/// that was imported (issue #127).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why an act-window and not a message.</b> The move waits for the answer and branches on it, exactly as
/// <see cref="IAccountBackupService"/> does for a deletion. It is injected as nullable for the same reason: a
/// panel composed without Backups has nothing to export from, and that must read as a fact and not as success.
/// </para>
/// <para>
/// Only the act crosses. The destination and its credentials stay inside the Backups module.
/// </para>
/// </remarks>
public interface IMigrationArtifactService
{
    /// <summary>Takes an unsealed backup of an account, kept for the export and never pruned by schedule retention.</summary>
    /// <param name="accountId">The account to export.</param>
    /// <param name="accountName">The account's system user name.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>The backup made, or the typed failure; a failure means there is nothing to export.</returns>
    Task<Result<MigrationExportArtifact>> CreateForExportAsync(
        Guid accountId,
        string accountName,
        CancellationToken cancellationToken);

    /// <summary>Removes the backup an export was made from, once the export has expired (issue #127, decision D11).</summary>
    /// <param name="backupId">The backup the export's artifact is read from.</param>
    /// <param name="accountName">The exported account's system user name, which the artifact is stored under.</param>
    /// <param name="cancellationToken">Cancels the removal.</param>
    /// <returns>
    /// Success when the artifact and its record are gone, including when they already were; otherwise the typed
    /// failure, and the caller keeps its own record so the removal is tried again.
    /// </returns>
    /// <remarks>
    /// Unattended work with no signed-in caller: the implementation reads the backup row without the tenant scope
    /// and removes only a backup of the export kind, so it cannot be aimed at an ordinary backup.
    /// </remarks>
    Task<Result> DiscardExportAsync(Guid backupId, string accountName, CancellationToken cancellationToken);

    /// <summary>
    /// Records an artifact the agent published for an import as a backup of the new account, and restores the
    /// account from it, loading its database dumps through the agent's restricted loader.
    /// </summary>
    /// <param name="artifact">What the agent published.</param>
    /// <param name="accountId">The account created for the import.</param>
    /// <param name="accountName">The account's system user name.</param>
    /// <param name="cancellationToken">Cancels the restore.</param>
    /// <returns>Success when the restore replaced the account's home and databases in full, otherwise the typed failure.</returns>
    Task<Result> RestoreImportedAsync(
        MigrationImportedArtifact artifact,
        Guid accountId,
        string accountName,
        CancellationToken cancellationToken);
}
