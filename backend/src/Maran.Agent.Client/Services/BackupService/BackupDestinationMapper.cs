using Maran.Agent.V1;
using Maran.SharedKernel.Security;

namespace Maran.Agent.Client.Services.BackupService;

/// <summary>Restates the panel's backup destinations and restore scopes on the wire, and names the secrets they hold.</summary>
internal static class BackupDestinationMapper
{
    /// <summary>Projects a panel destination onto the wire message, revealing the secrets it holds.</summary>
    /// <param name="destination">The destination the caller named.</param>
    /// <param name="includeArchiveKey">
    /// True for the three calls that seal or open an archive, the only ones that carry a local destination's
    /// archive key; false for every other call, which the agent would ignore the key on.
    /// </param>
    /// <returns>The wire message, which never leaves this project.</returns>
    /// <remarks>
    /// The two credentials are revealed here and nowhere else in this client, which is what makes
    /// "where does the secret escape" a one-search question. The wire message is flat, so the
    /// members of a case that has none of them are simply left at the wire's defaults — the agent
    /// refuses a local destination that names anything, and a branch that blanked a field would be a
    /// second place the shape of a local destination is decided.
    /// </remarks>
    public static BackupDestination ToWireDestination(AgentBackupDestination destination, bool includeArchiveKey)
    {
        if (destination is AgentS3BackupDestination s3)
        {
            return new BackupDestination
            {
                Kind = BackupDestinationKind.S3,
                Path = s3.Path,
                S3Bucket = s3.Bucket,
                S3Region = s3.Region,
                S3Endpoint = s3.Endpoint,
                S3AccessKeyId = s3.AccessKeyId.Reveal(),
                S3SecretAccessKey = s3.SecretAccessKey.Reveal(),
                S3PathStyle = s3.PathStyle,
            };
        }

        var local = new BackupDestination { Kind = BackupDestinationKind.Local };

        // Only the three calls that seal or open an archive carry the key; the agent reads it from no
        // other (backup.proto), and a field it would ignore is a field a caller believes in.
        if (includeArchiveKey && destination is AgentLocalBackupDestination { ArchiveKey: { } archiveKey })
        {
            local.ArchiveKey = archiveKey.Reveal();
        }

        return local;
    }

    /// <summary>Projects a panel restore scope onto the wire message.</summary>
    /// <param name="scope">What the restore replaces.</param>
    /// <returns>
    /// <c>null</c> for the whole account, which is the absent message an agent from before scopes
    /// existed reads the same way; otherwise a message carrying the one name the scope holds.
    /// </returns>
    /// <remarks>
    /// An unknown case is refused rather than collapsed to the whole account: a scope this client was
    /// never taught is a bug, and sending the widest restore for it would replace more than was asked.
    /// </remarks>
    public static RestoreScope? ToWireScope(AgentRestoreScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return scope switch
        {
            AgentWholeRestoreScope => null,
            AgentDatabaseRestoreScope database => new RestoreScope { DatabaseName = database.DatabaseName },
            AgentSiteRestoreScope site => new RestoreScope { SiteDomain = site.Domain },
            _ => throw new ArgumentOutOfRangeException(
                nameof(scope), scope, "Unknown restore scope; the hierarchy is closed to three cases."),
        };
    }

    /// <summary>Names the one secret a destination holds, so a translator can redact it from a quote.</summary>
    /// <param name="destination">The destination the caller named.</param>
    /// <returns>
    /// The secret access key of an object store, the archive key of a local destination that carries
    /// one, or <c>null</c> for a destination that holds neither.
    /// </returns>
    public static SensitiveString? SecretOf(AgentBackupDestination destination)
    {
        return destination switch
        {
            AgentS3BackupDestination s3 => s3.SecretAccessKey,
            AgentLocalBackupDestination { ArchiveKey: { } archiveKey } => archiveKey,
            _ => null,
        };
    }
}
