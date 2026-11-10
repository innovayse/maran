using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Maran.Modules.ServerMigration.Domain.Policies;
using Maran.Modules.ServerMigration.Models;
using Maran.Modules.ServerMigration.Resources;
using Maran.SharedKernel.Utilities.Signatures;

namespace Maran.Modules.ServerMigration.Services;

/// <summary>
/// Reads the head of a package from an untrusted stream and refuses anything that is not exactly what
/// <see cref="MigrationPackageWriter"/> writes (issue #127).
/// </summary>
/// <remarks>
/// <para>
/// <b>Strict, and in an order that spends nothing before it has checked.</b> Every length is bounded before a
/// buffer is allocated for it; the public key is read and its fingerprint compared with the one the operator
/// confirmed before any header is parsed; the panel's signature is verified before its JSON is looked at; the
/// agent's manifest is verified by the same key and bound to the header by its digest before its figures are
/// trusted. Unknown schema, a short stream and a manifest that disagrees with the header are refusals, never
/// repairs.
/// </para>
/// <para>
/// <b>What it leaves in the stream.</b> After a successful read the stream is positioned at the first artifact
/// byte, and the caller forwards exactly <see cref="AgentTransferManifest.ArtifactBytes"/> of them and then
/// confirms the stream holds nothing more (<see cref="HasTrailingBytesAsync"/>). The artifact is never buffered
/// here.
/// </para>
/// <para>
/// A valid signature proves only who signed. Whether that is the server the operator believes it is rests on the
/// fingerprint the operator compared, which is why it is an argument and not an optional one.
/// </para>
/// </remarks>
public static class MigrationPackageReader
{
    /// <summary>The only header schema this reader understands.</summary>
    public const int SupportedSchema = 2;

    /// <summary>The ten bytes every package starts with.</summary>
    public static ReadOnlySpan<byte> Magic
    {
        get { return "MARANPKG2\n"u8; }
    }

    /// <summary>Reads and verifies the head of a package.</summary>
    /// <param name="input">The package, positioned at its first byte.</param>
    /// <param name="confirmedFingerprint">The fingerprint of the sending server's key that the operator confirmed.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The verified head with the stream left at the artifact, or the first refusal.</returns>
    public static async Task<Result<VerifiedPackage>> ReadAsync(
        Stream input,
        string confirmedFingerprint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var magic = await ReadExactlyAsync(input, Magic.Length, cancellationToken);

        if (magic is null || !magic.AsSpan().SequenceEqual(Magic))
        {
            return Refuse(nameof(ErrorMessages.MigrationPackageMalformed), ErrorType.Validation);
        }

        var publicKey = await ReadLengthPrefixedAsync(input, 2, EcdsaP384Signature.UncompressedPointBytes, cancellationToken);

        if (publicKey is null || publicKey.Length != EcdsaP384Signature.UncompressedPointBytes)
        {
            return Refuse(nameof(ErrorMessages.MigrationPackageMalformed), ErrorType.Validation);
        }

        var fingerprint = PublicKeyFingerprint.Of(publicKey);

        // Ordinal and case-sensitive, exactly as the agent compares it: a fingerprint the operator did not
        // type is not a near miss to be forgiven.
        if (!string.Equals(fingerprint, confirmedFingerprint, StringComparison.Ordinal))
        {
            return Refuse(nameof(ErrorMessages.MigrationFingerprintMismatch), ErrorType.Conflict);
        }

        var headerBytes = await ReadLengthPrefixedAsync(input, 4, MigrationPackageLimits.MaxHeaderBytes, cancellationToken);
        var headerSignature = await ReadLengthPrefixedAsync(input, 2, MigrationPackageLimits.MaxSignatureBytes, cancellationToken);

        if (headerBytes is null || headerSignature is null)
        {
            return Refuse(nameof(ErrorMessages.MigrationPackageMalformed), ErrorType.Validation);
        }

        if (!EcdsaP384Signature.VerifyUncompressedPoint(publicKey, headerBytes, headerSignature))
        {
            return Refuse(nameof(ErrorMessages.MigrationSignatureInvalid), ErrorType.Validation);
        }

        var header = ParseHeader(headerBytes);

        if (header is null || header.KeyFingerprint != fingerprint)
        {
            return Refuse(nameof(ErrorMessages.MigrationPackageMalformed), ErrorType.Validation);
        }

        if (header.Schema != SupportedSchema)
        {
            return Refuse(nameof(ErrorMessages.MigrationPackageSchemaUnknown), ErrorType.Validation);
        }

        var manifestBytes = await ReadLengthPrefixedAsync(input, 4, MigrationPackageLimits.MaxHeaderBytes, cancellationToken);
        var manifestSignature = await ReadLengthPrefixedAsync(input, 2, MigrationPackageLimits.MaxSignatureBytes, cancellationToken);

        if (manifestBytes is null || manifestSignature is null)
        {
            return Refuse(nameof(ErrorMessages.MigrationPackageMalformed), ErrorType.Validation);
        }

        if (!EcdsaP384Signature.VerifyUncompressedPoint(publicKey, manifestBytes, manifestSignature))
        {
            return Refuse(nameof(ErrorMessages.MigrationSignatureInvalid), ErrorType.Validation);
        }

        if (!string.Equals(
                Convert.ToHexStringLower(SHA256.HashData(manifestBytes)),
                header.AgentManifestSha256,
                StringComparison.Ordinal))
        {
            return Refuse(nameof(ErrorMessages.MigrationPackageMalformed), ErrorType.Validation);
        }

        var manifest = ParseManifest(manifestBytes);

        if (manifest is null || !string.Equals(manifest.Account, header.AccountName, StringComparison.Ordinal))
        {
            return Refuse(nameof(ErrorMessages.MigrationPackageMalformed), ErrorType.Validation);
        }

        if (manifest.ArtifactBytes > MigrationPackageLimits.MaxArtifactBytes)
        {
            return Refuse(nameof(ErrorMessages.MigrationPackageTooLarge), ErrorType.Validation);
        }

        return Result<VerifiedPackage>.Ok(
            new VerifiedPackage(
                header,
                manifest,
                manifestBytes,
                manifestSignature,
                publicKey,
                JsonSerializer.Serialize(header.Sections, JsonSerializerOptions.Web)));
    }

    /// <summary>Tells whether the stream holds anything after the artifact.</summary>
    /// <param name="input">The package, positioned after the last artifact byte.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><c>true</c> when at least one more byte is there, which makes the package malformed.</returns>
    public static async Task<bool> HasTrailingBytesAsync(Stream input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var one = new byte[1];

        return await input.ReadAsync(one, cancellationToken) > 0;
    }

    /// <summary>Reads a length-prefixed field, refusing a length above the bound before allocating for it.</summary>
    /// <param name="input">The stream.</param>
    /// <param name="prefixBytes">2 for a 16-bit big-endian length, 4 for a 32-bit one.</param>
    /// <param name="maxLength">The most bytes the field may hold.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The field, or <c>null</c> when the stream is short, the length is zero or it is over the bound.</returns>
    private static async Task<byte[]?> ReadLengthPrefixedAsync(
        Stream input,
        int prefixBytes,
        int maxLength,
        CancellationToken cancellationToken)
    {
        var prefix = await ReadExactlyAsync(input, prefixBytes, cancellationToken);

        if (prefix is null)
        {
            return null;
        }

        long length = prefixBytes == 2
            ? BinaryPrimitives.ReadUInt16BigEndian(prefix)
            : BinaryPrimitives.ReadUInt32BigEndian(prefix);

        if (length < 1 || length > maxLength)
        {
            return null;
        }

        return await ReadExactlyAsync(input, (int)length, cancellationToken);
    }

    /// <summary>Reads exactly a number of bytes.</summary>
    /// <param name="input">The stream.</param>
    /// <param name="count">How many bytes.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The bytes, or <c>null</c> when the stream ended first.</returns>
    private static async Task<byte[]?> ReadExactlyAsync(Stream input, int count, CancellationToken cancellationToken)
    {
        var buffer = new byte[count];

        try
        {
            await input.ReadExactlyAsync(buffer, cancellationToken);
        }
        catch (EndOfStreamException)
        {
            return null;
        }

        return buffer;
    }

    /// <summary>Reads the panel's header from bytes whose signature has already verified.</summary>
    /// <param name="bytes">The signed bytes.</param>
    /// <returns>The header, or <c>null</c> when it is not the shape the writer produces.</returns>
    private static MigrationPackageHeader? ParseHeader(byte[] bytes)
    {
        try
        {
            return JsonSerializer.Deserialize<MigrationPackageHeader>(bytes, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads the figures the panel acts on out of the agent's manifest, whose signature has already verified.</summary>
    /// <param name="bytes">The exact bytes the agent signed.</param>
    /// <returns>The figures, or <c>null</c> when a field is missing, of the wrong type or out of range.</returns>
    private static AgentTransferManifest? ParseManifest(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !Guid.TryParse(root.GetProperty("backup_id").GetString(), out var backupId)
                || root.GetProperty("account").GetString() is not { Length: > 0 } account
                || root.GetProperty("artifact_sha256").GetString() is not { Length: 64 } sha256
                || !root.GetProperty("artifact_bytes").TryGetInt64(out var artifactBytes)
                || !root.GetProperty("home_bytes").TryGetInt64(out var homeBytes)
                || artifactBytes < 1
                || homeBytes < 0)
            {
                return null;
            }

            return new AgentTransferManifest(
                backupId,
                account,
                artifactBytes,
                sha256,
                homeBytes,
                root.GetProperty("databases").GetArrayLength());
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>A refusal as a failed result.</summary>
    /// <param name="code">The error code.</param>
    /// <param name="type">The kind of failure.</param>
    /// <returns>The failed result.</returns>
    private static Result<VerifiedPackage> Refuse(string code, ErrorType type)
    {
        return Result<VerifiedPackage>.Fail(Error.Of(code, type));
    }
}
