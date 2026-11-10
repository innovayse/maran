using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.TransferService;
using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Models;
using Maran.Modules.ServerMigration.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.ServerMigration.Services;

/// <summary>
/// Writes an export as a package: this server's signed header, the agent's signed manifest and the artifact,
/// streamed without buffering (issue #127).
/// </summary>
/// <remarks>
/// <para>
/// <b>The agent signs the artifact's manifest, the panel signs the sections, and one key does both.</b> The
/// panel hands its signing key to the agent for the length of the export call; the agent states the manifest
/// of the artifact it is about to send and signs it; the panel checks that the key in that header is its own,
/// binds its header to the manifest by digest, signs the header, and writes the head before the first chunk.
/// </para>
/// <para>
/// <b>A failure after the head is written truncates the stream</b> and cannot be reported in a status that has
/// already been sent. That is safe by construction: the receiver reads exactly the signed number of artifact
/// bytes and the agent refuses a short stream, so a truncated package is a refused one.
/// </para>
/// </remarks>
public sealed class MigrationPackageWriter
{
    /// <summary>The agent's transfer client.</summary>
    private readonly IAgentTransferClient _agent;

    /// <summary>This server's signing key.</summary>
    private readonly MigrationSigningKeys _keys;

    /// <summary>Tells the name this server gives itself.</summary>
    private readonly IPanelHostName _hostName;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the writer.</summary>
    /// <param name="agent">The agent's transfer client.</param>
    /// <param name="keys">This server's signing key.</param>
    /// <param name="hostName">Tells the name this server gives itself.</param>
    /// <param name="clock">The panel's clock.</param>
    public MigrationPackageWriter(
        IAgentTransferClient agent,
        MigrationSigningKeys keys,
        IPanelHostName hostName,
        IClock clock)
    {
        _agent = agent;
        _keys = keys;
        _hostName = hostName;
        _clock = clock;
    }

    /// <summary>Writes the package of an export, or only the head of it.</summary>
    /// <param name="export">The export to write.</param>
    /// <param name="output">Where the package goes, and how it is told the size before the first byte.</param>
    /// <param name="extent">Whether to write the head alone (a preview) or the head and the whole artifact.</param>
    /// <param name="cancellationToken">Cancels the export.</param>
    /// <returns>
    /// Success when everything <paramref name="extent"/> asks for was written, otherwise the first failure. A head
    /// is written as soon as the agent has signed its manifest, and the agent's export is then abandoned: the
    /// call is classed "stop" on the agent, so no artifact byte is read beyond what the agent already queued.
    /// </returns>
    public async Task<Result> WriteAsync(
        AccountExport export,
        PackageDestination output,
        PackageExtent extent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(export);
        ArgumentNullException.ThrowIfNull(output);

        var key = await _keys.GetOrCreateAsync(cancellationToken);
        var sections = JsonSerializer.Deserialize<List<MigrationSection>>(export.SectionsJson, JsonSerializerOptions.Web) ?? [];
        var wroteHead = false;
        long written = 0;
        long declared = 0;

        await foreach (var exported in _agent.ExportBackupAsync(
            export.SourceAccountName,
            export.BackupId.ToString(),
            key.PrivateKey,
            cancellationToken))
        {
            switch (exported)
            {
                case AgentBackupExportHeader header when !wroteHead:
                    // The agent signed with the key it was given; a header that names another is not ours.
                    if (!header.PublicKey.AsSpan().SequenceEqual(key.PublicKey))
                    {
                        return Result.Fail(Error.Of(nameof(ErrorMessages.MigrationSignatureInvalid), ErrorType.Failure));
                    }

                    declared = ReadManifestBytes(header.Manifest, "artifact_bytes");
                    await WriteHeadAsync(export, key, header, sections, output, extent == PackageExtent.Whole ? declared : 0, cancellationToken);
                    wroteHead = true;

                    if (extent == PackageExtent.HeadOnly)
                    {
                        return Result.Ok();
                    }

                    break;

                case AgentBackupExportChunk chunk when wroteHead:
                    await output.Body.WriteAsync(chunk.Data, cancellationToken);
                    written += chunk.Data.Length;
                    break;

                case AgentBackupExportDone done when wroteHead:
                    return written == done.ArtifactBytes && written == declared
                        ? Result.Ok()
                        : Result.Fail(Error.Of(nameof(ErrorMessages.MigrationPackageMalformed), ErrorType.Failure));

                case AgentBackupExportFailed failed:
                    return Result.Fail(failed.Error);

                default:
                    return Result.Fail(Error.Of(nameof(ErrorMessages.MigrationPackageMalformed), ErrorType.Failure));
            }
        }

        // The stream ended with no terminal event; the client guarantees one, so this is a defect, and the
        // artifact is treated as incomplete.
        return Result.Fail(Error.Of(nameof(ErrorMessages.MigrationPackageMalformed), ErrorType.Failure));
    }

    /// <summary>Writes everything before the first artifact byte.</summary>
    /// <param name="export">The export.</param>
    /// <param name="key">This server's key.</param>
    /// <param name="agentHeader">The agent's signed manifest.</param>
    /// <param name="sections">What the modules contributed.</param>
    /// <param name="output">Where the package goes.</param>
    /// <param name="artifactBytes">How many artifact bytes will follow the head: zero for a head alone.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Resolves when the head is written, after the destination was told the size of what is coming.</returns>
    private async Task WriteHeadAsync(
        AccountExport export,
        ServerSigningKey key,
        AgentBackupExportHeader agentHeader,
        List<MigrationSection> sections,
        PackageDestination output,
        long artifactBytes,
        CancellationToken cancellationToken)
    {
        _ = _hostName.TryGet(out var hostName);

        var header = new MigrationPackageHeader(
            MigrationPackageReader.SupportedSchema,
            hostName ?? string.Empty,
            key.Fingerprint,
            export.SourceAccountName,
            ReadManifestBytes(agentHeader.Manifest, "home_bytes"),
            Convert.ToHexStringLower(SHA256.HashData(agentHeader.Manifest)),
            _clock.UtcNow,
            sections);
        var headerBytes = JsonSerializer.SerializeToUtf8Bytes(header, JsonSerializerOptions.Web);
        var headerSignature = MigrationSigningKeys.Sign(key, headerBytes);

        // The head is built in memory first, because its size is part of the package's size and the destination is
        // told that size before any byte leaves. It is bounded: two documents of at most MaxHeaderBytes each.
        using var head = new MemoryStream();
        await head.WriteAsync(MigrationPackageReader.Magic.ToArray(), cancellationToken);
        await WriteFieldAsync(head, 2, key.PublicKey, cancellationToken);
        await WriteFieldAsync(head, 4, headerBytes, cancellationToken);
        await WriteFieldAsync(head, 2, headerSignature, cancellationToken);
        await WriteFieldAsync(head, 4, agentHeader.Manifest, cancellationToken);
        await WriteFieldAsync(head, 2, agentHeader.Signature, cancellationToken);

        output.OnLengthKnown(head.Length + artifactBytes);
        head.Position = 0;
        await head.CopyToAsync(output.Body, cancellationToken);
    }

    /// <summary>Writes one length-prefixed field.</summary>
    /// <param name="output">Where the package goes.</param>
    /// <param name="prefixBytes">2 for a 16-bit big-endian length, 4 for a 32-bit one.</param>
    /// <param name="field">The field's bytes.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Resolves when the field is written.</returns>
    private static async Task WriteFieldAsync(Stream output, int prefixBytes, byte[] field, CancellationToken cancellationToken)
    {
        var prefix = new byte[prefixBytes];

        if (prefixBytes == 2)
        {
            BinaryPrimitives.WriteUInt16BigEndian(prefix, (ushort)field.Length);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(prefix, (uint)field.Length);
        }

        await output.WriteAsync(prefix, cancellationToken);
        await output.WriteAsync(field, cancellationToken);
    }

    /// <summary>Reads a size the agent declared in its manifest.</summary>
    /// <param name="manifest">The manifest bytes the agent signed.</param>
    /// <param name="name">The manifest's field: <c>artifact_bytes</c> or <c>home_bytes</c>.</param>
    /// <returns>The declared size.</returns>
    private static long ReadManifestBytes(byte[] manifest, string name)
    {
        using var document = JsonDocument.Parse(manifest);

        return document.RootElement.GetProperty(name).GetInt64();
    }
}
