using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.BackupService;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Models;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Backups.Services;

/// <summary>
/// This module's implementation of <see cref="IMigrationArtifactService"/>: the backup an account export is
/// made from, and the restore of an artifact an import published (issue #127).
/// </summary>
/// <remarks>
/// <para>
/// <b>The export copy is an ordinary backup of kind <see cref="BackupKind.Migration"/></b>, taken through
/// <see cref="SafetyBackupService"/> so the destination resolution, the run, the verification and the journal
/// are the ones every backup has. The agent refuses to export a sealed artifact (its bytes would be unreadable
/// on the receiving host), and that refusal reaches the caller as the agent's own typed failure.
/// </para>
/// <para>
/// <b>An imported artifact becomes a backup row only after its account exists.</b> The agent publishes it
/// before the account is created (an import is refused if the system user already exists), so the row is
/// written here, under the SOURCE backup id the agent used, once the account does. The restore that follows is
/// the ordinary one, with the databases the importers just created as its allow-list; it loads the imported
/// dumps through the agent's restricted loader because the agent marked the artifact as imported, and no
/// request from the panel can switch that off.
/// </para>
/// </remarks>
public sealed class MigrationArtifactService : IMigrationArtifactService
{
    /// <summary>The one path that takes a safety-style backup.</summary>
    private readonly SafetyBackupService _safetyBackups;

    /// <summary>The module's database context.</summary>
    private readonly BackupsDbContext _dbContext;

    /// <summary>Resolves where an artifact is read from.</summary>
    private readonly BackupDestinationResolver _destinations;

    /// <summary>Runs the agent's restore.</summary>
    private readonly RestoreRunner _restoreRunner;

    /// <summary>Lists the databases the panel's own rows name for an account.</summary>
    private readonly IAccountDatabaseDirectory _databases;

    /// <summary>The agent, which owns every byte of every archive.</summary>
    private readonly IAgentBackupClient _agent;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the service.</summary>
    /// <param name="safetyBackups">The one path that takes a safety-style backup.</param>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="destinations">Resolves where an artifact is read from.</param>
    /// <param name="restoreRunner">Runs the agent's restore.</param>
    /// <param name="databases">Lists the databases the panel's own rows name for an account.</param>
    /// <param name="agent">The agent, which owns every byte of every archive.</param>
    /// <param name="clock">The panel's clock.</param>
    public MigrationArtifactService(
        SafetyBackupService safetyBackups,
        BackupsDbContext dbContext,
        BackupDestinationResolver destinations,
        RestoreRunner restoreRunner,
        IAccountDatabaseDirectory databases,
        IAgentBackupClient agent,
        IClock clock)
    {
        _safetyBackups = safetyBackups;
        _dbContext = dbContext;
        _destinations = destinations;
        _restoreRunner = restoreRunner;
        _databases = databases;
        _agent = agent;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<MigrationExportArtifact>> CreateForExportAsync(
        Guid accountId,
        string accountName,
        CancellationToken cancellationToken)
    {
        var taken = await _safetyBackups.TakeAsync(
            BackupKind.Migration,
            accountId,
            accountName,
            BackupAuditActions.MigrationBackupTaken,
            nameof(ErrorMessages.MigrationBackupFailed),
            Guid.Empty,
            string.Empty,
            string.Empty,
            cancellationToken);

        if (!taken.IsSuccess)
        {
            return Result<MigrationExportArtifact>.Fail(taken.Error!);
        }

        var backup = await _dbContext.Backups.AsNoTracking().FirstAsync(row => row.Id == taken.Value, cancellationToken);

        // A completed row always carries its digest (Completed refuses a run that reported none), so a null
        // here is a row this module did not write; refused rather than exported unverifiable.
        return backup.ArtifactSha256 is null
            ? Result<MigrationExportArtifact>.Fail(Error.Of(nameof(ErrorMessages.MigrationBackupFailed), ErrorType.Failure))
            : Result<MigrationExportArtifact>.Ok(
                new MigrationExportArtifact(backup.Id, backup.SizeBytes, backup.ArtifactSha256, backup.DatabaseCount));
    }

    /// <inheritdoc />
    public async Task<Result> DiscardExportAsync(Guid backupId, string accountName, CancellationToken cancellationToken)
    {
        // The row is read without the tenant scope because the expiry runs with no principal, and a scoped read
        // would find nothing, every night, for ever. Only a backup of the export kind is ever removed here.
#pragma warning disable RS0030 // unattended expiry of an export has no principal; scoped, it would remove nothing
        var backup = await _dbContext.Backups
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(row => row.Id == backupId && row.Kind == BackupKind.Migration, cancellationToken);
#pragma warning restore RS0030

        // No row: the account's deletion already removed the artifact with it, or this ran before. Gone is done.
        if (backup is null)
        {
            return Result.Ok();
        }

        if (!backup.MayBeDeleted())
        {
            return Result.Fail(Error.Of(nameof(ErrorMessages.BackupStillRunning), ErrorType.Conflict));
        }

        var destination = await _destinations.ResolveAsync(backup.DestinationId, cancellationToken);

        if (!destination.IsSuccess)
        {
            return Result.Fail(destination.Error!);
        }

        var deleted = await _agent.DeleteAsync(accountName, backup.Id.ToString(), destination.Value!.Agent, cancellationToken);

        // The agent answers not-found for an artifact it cannot see; the caller asked for it not to exist.
        if (!deleted.IsSuccess && deleted.Error!.Type != ErrorType.NotFound)
        {
            return Result.Fail(deleted.Error!);
        }

        _dbContext.Backups.Remove(backup);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Ok();
    }

    /// <inheritdoc />
    public async Task<Result> RestoreImportedAsync(
        MigrationImportedArtifact artifact,
        Guid accountId,
        string accountName,
        CancellationToken cancellationToken)
    {
        var destination = await _destinations.ResolveAsync(destinationId: null, cancellationToken);

        if (!destination.IsSuccess)
        {
            return Result.Fail(destination.Error!);
        }

        // A backup row is recorded once for an id; an artifact id that is already a row here (an import of an
        // export made on this very server, or a repeat) is refused rather than overwritten.
        if (await _dbContext.Backups.AnyAsync(row => row.Id == artifact.BackupId, cancellationToken))
        {
            return Result.Fail(Error.Of(MigrationCodes.BackupIdTaken, ErrorType.Conflict));
        }

        var backup = new Backup(artifact.BackupId, accountId, destination.Value!.Id, BackupKind.Migration, _clock.UtcNow);
        backup.Completed(artifact.SizeBytes, artifact.Sha256, artifact.DatabaseCount, _clock.UtcNow);
        _dbContext.Backups.Add(backup);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // The panel's own rows, never the manifest's: a database the dump names and the panel has not created
        // is refused by the agent and never created by a restore.
        var allowedDatabases = await _databases.ListNamesAsync(accountId, cancellationToken);

        var outcome = await _restoreRunner.RunAsync(
            accountName,
            artifact.BackupId,
            artifact.Sha256,
            allowedDatabases,
            destination.Value.Agent,
            new AgentWholeRestoreScope(),
            Guid.Empty,
            cancellationToken);

        return outcome switch
        {
            WholeRestoreOutcome => Result.Ok(),
            PartialRestoreOutcome partial => Result.Fail(partial.Failure),
            UnmeasuredRestoreOutcome unmeasured => Result.Fail(unmeasured.Failure),
            _ => throw new InvalidOperationException(
                $"Unhandled restore outcome {outcome.GetType().Name}; the hierarchy is closed to three cases."),
        };
    }
}
