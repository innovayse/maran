using System.Runtime.CompilerServices;
using Google.Protobuf;
using Maran.Agent.V1;
using Maran.SharedKernel.Security;
using WireRefusal = Maran.Agent.V1.ImportRefusalReason;

namespace Maran.Agent.Client.Services.TransferService;

/// <summary>Restates an artifact export and import on the wire and reads the agent's refusals back.</summary>
/// <remarks>
/// Beside the source mapper, not inside it: moving an artifact has nothing to do with a registered source,
/// and the signing key is the only secret this half handles. It is revealed here once, when the request is
/// put on the wire.
/// </remarks>
internal static class BackupTransferMapper
{
    /// <summary>Projects an export request onto the wire message, revealing the signing key.</summary>
    /// <param name="accountUsername">System user name of the account whose backup is exported.</param>
    /// <param name="backupId">Id of the published backup.</param>
    /// <param name="signingKey">The per-server package signing key: PKCS#8 DER, base64-encoded.</param>
    /// <returns>The wire message, which never leaves this project.</returns>
    /// <exception cref="FormatException">The key is not base64; the panel stores only what it generated itself.</exception>
    public static ExportBackupRequest ToWire(string accountUsername, string backupId, SensitiveString signingKey)
    {
        ArgumentNullException.ThrowIfNull(signingKey);

        return new ExportBackupRequest
        {
            AccountUsername = accountUsername,
            BackupId = backupId,
            SigningKey = ByteString.CopyFrom(Convert.FromBase64String(signingKey.Reveal())),
        };
    }

    /// <summary>Streams the wire messages of an import: the header, then each chunk.</summary>
    /// <param name="header">What the import carries before the first artifact byte.</param>
    /// <param name="chunks">The artifact, in order.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>The messages in the order the agent requires.</returns>
    public static async IAsyncEnumerable<ImportBackupRequest> ToWireAsync(
        AgentBackupImportHeader header,
        IAsyncEnumerable<ReadOnlyMemory<byte>> chunks,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(chunks);

        yield return new ImportBackupRequest
        {
            Header = new ImportBackupHeader
            {
                Manifest = ByteString.CopyFrom(header.Manifest),
                Signature = ByteString.CopyFrom(header.Signature),
                PublicKey = ByteString.CopyFrom(header.PublicKey),
                ExpectedKeyFingerprint = header.ExpectedKeyFingerprint,
                MaxBytes = unchecked((ulong)Math.Max(0, header.MaxBytes)),
            },
        };

        await foreach (var chunk in chunks.WithCancellation(cancellationToken))
        {
            if (chunk.IsEmpty)
            {
                continue;
            }

            yield return new ImportBackupRequest { Chunk = ByteString.CopyFrom(chunk.Span) };
        }
    }

    /// <summary>Reads the agent's published artifact, refusing one that cannot be read in full.</summary>
    /// <param name="ok">The wire message.</param>
    /// <returns>The published artifact, or <c>null</c> when a quantity does not fit the panel's type or a field is missing.</returns>
    public static AgentBackupImported? ToImported(ImportBackupOk ok)
    {
        if (string.IsNullOrEmpty(ok.BackupId)
            || string.IsNullOrEmpty(ok.ArtifactSha256)
            || ok.ArtifactBytes > long.MaxValue
            || ok.DatabaseCount > int.MaxValue)
        {
            return null;
        }

        return new AgentBackupImported(
            ok.BackupId,
            (long)ok.ArtifactBytes,
            ok.ArtifactSha256,
            (int)ok.DatabaseCount,
            ok.AlreadyPresent);
    }

    /// <summary>Reads the reason an import was refused.</summary>
    /// <param name="reason">The wire reason.</param>
    /// <returns>The panel's reason; one this build has no member for reads as unspecified, the error code beside it still states what happened.</returns>
    public static BackupImportRefusal ToRefusal(WireRefusal reason)
    {
        return reason switch
        {
            WireRefusal.SignatureInvalid => BackupImportRefusal.SignatureInvalid,
            WireRefusal.FingerprintMismatch => BackupImportRefusal.FingerprintMismatch,
            WireRefusal.ManifestMalformed => BackupImportRefusal.ManifestMalformed,
            WireRefusal.AccountExists => BackupImportRefusal.AccountExists,
            WireRefusal.BackupIdTaken => BackupImportRefusal.BackupIdTaken,
            WireRefusal.TooLarge => BackupImportRefusal.TooLarge,
            WireRefusal.LengthMismatch => BackupImportRefusal.LengthMismatch,
            WireRefusal.DigestMismatch => BackupImportRefusal.DigestMismatch,
            WireRefusal.ArtifactRefused => BackupImportRefusal.ArtifactRefused,
            _ => BackupImportRefusal.Unspecified,
        };
    }
}
