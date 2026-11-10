using Maran.Agent.Client.Resources;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Services.BackupService;

/// <summary>Reads the wire's backup listing into the panel's summaries, refusing an entry the contract forbids.</summary>
internal static class BackupListingMapper
{
    /// <summary>Projects the wire listing onto the panel's summaries, refusing an entry the contract forbids.</summary>
    /// <param name="ok">The success payload of <c>ListBackups</c>.</param>
    /// <returns>
    /// The summaries in the order the agent sent them, or an invalid-response failure when an entry
    /// claims to be readable and carries no manifest.
    /// </returns>
    /// <remarks>
    /// The unset-arm case fails for the same reason and by the same rule: proto3 lets a oneof carry
    /// no arm at all, and an entry that says neither "here is what it holds" nor "here is why I
    /// cannot say" is not an entry a caller can act on.
    /// </remarks>
    public static Result<IReadOnlyList<AgentBackupSummary>> ToSummariesResult(ListBackupsOk ok)
    {
        var summaries = new List<AgentBackupSummary>(ok.Backups.Count);
        foreach (var backup in ok.Backups)
        {
            if (backup.StateCase == BackupInfo.StateOneofCase.Readable)
            {
                if (backup.Readable.Manifest is null)
                {
                    return Result<IReadOnlyList<AgentBackupSummary>>.Fail(
                        Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure));
                }

                summaries.Add(new AgentBackupSummary(
                    backup.BackupId,
                    new AgentReadableBackup(
                        ToManifest(backup.Readable.Manifest),
                        backup.Readable.ArtifactBytes,
                        backup.Readable.ArtifactSha256),
                    null));
                continue;
            }

            if (backup.StateCase == BackupInfo.StateOneofCase.Unreadable)
            {
                // A kind the wire left unspecified, or one a newer agent added, is a listing this
                // build cannot describe truthfully: refused whole rather than given a reason the
                // agent never gave.
                var reason = ToUnreadableReason(backup.Unreadable);
                if (reason is null)
                {
                    return Result<IReadOnlyList<AgentBackupSummary>>.Fail(
                        Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure));
                }

                summaries.Add(new AgentBackupSummary(backup.BackupId, null, reason));
                continue;
            }

            return Result<IReadOnlyList<AgentBackupSummary>>.Fail(
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure));
        }

        return Result<IReadOnlyList<AgentBackupSummary>>.Ok(summaries);
    }

    /// <summary>Projects a wire manifest onto the panel's own.</summary>
    /// <param name="manifest">The manifest the sidecar carried.</param>
    /// <returns>The panel-side manifest, database lines included.</returns>
    private static AgentBackupManifest ToManifest(BackupManifest manifest)
    {
        var databases = new List<AgentManifestDatabase>(manifest.Databases.Count);
        foreach (var database in manifest.Databases)
        {
            databases.Add(new AgentManifestDatabase(database.Name, database.Bytes, database.Sha256));
        }

        return new AgentBackupManifest(
            manifest.Version,
            manifest.Account,
            manifest.BackupId,
            manifest.CreatedAtUnix,
            manifest.HomeBytes,
            databases,
            manifest.AgentVersion);
    }

    /// <summary>Maps the wire's unreadable reason onto the panel's.</summary>
    /// <param name="reason">The reason the agent reported.</param>
    /// <returns>
    /// The matching case, carrying the version only for an unknown version; <c>null</c> for the
    /// wire's unspecified value and for a kind a newer agent added — never a guess at one of the three.
    /// </returns>
    private static AgentBackupUnreadableReason? ToUnreadableReason(UnreadableReason reason)
    {
        return reason.Kind switch
        {
            UnreadableKind.Corrupt => new AgentBackupCorrupt(),
            UnreadableKind.UnknownVersion => new AgentBackupUnknownVersion(reason.Version),
            UnreadableKind.NotARegularFile => new AgentBackupNotARegularFile(),
            _ => null,
        };
    }
}
