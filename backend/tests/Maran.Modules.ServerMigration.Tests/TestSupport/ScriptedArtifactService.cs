using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Results;

namespace Maran.Modules.ServerMigration.Tests.TestSupport;

/// <summary>The Backups window with its restore outcome set by the test.</summary>
public sealed class ScriptedArtifactService : IMigrationArtifactService
{
    /// <summary>The error the restore answers, or <c>null</c> to succeed.</summary>
    public Error? RestoreFailsWith { get; set; }

    /// <summary>How many restores were asked for.</summary>
    public int Restores { get; private set; }

    /// <summary>The error the discard answers, or <c>null</c> to succeed.</summary>
    public Error? DiscardFailsWith { get; set; }

    /// <summary>The export artifacts that were asked to be discarded.</summary>
    public List<(Guid BackupId, string AccountName)> Discarded { get; } = [];

    /// <inheritdoc />
    public Task<Result> DiscardExportAsync(Guid backupId, string accountName, CancellationToken cancellationToken)
    {
        Discarded.Add((backupId, accountName));

        return Task.FromResult(DiscardFailsWith is null ? Result.Ok() : Result.Fail(DiscardFailsWith));
    }

    /// <inheritdoc />
    public Task<Result<MigrationExportArtifact>> CreateForExportAsync(
        Guid accountId,
        string accountName,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(
            Result<MigrationExportArtifact>.Ok(new MigrationExportArtifact(Guid.NewGuid(), 1, new string('d', 64), 0)));
    }

    /// <inheritdoc />
    public Task<Result> RestoreImportedAsync(
        MigrationImportedArtifact artifact,
        Guid accountId,
        string accountName,
        CancellationToken cancellationToken)
    {
        Restores++;

        return Task.FromResult(RestoreFailsWith is null ? Result.Ok() : Result.Fail(RestoreFailsWith));
    }
}
