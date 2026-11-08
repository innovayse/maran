using System.Runtime.CompilerServices;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.BackupService;
using Maran.Agent.Simulated.Errors;
using Maran.Agent.Simulated.State;

namespace Maran.Agent.Simulated.Services.BackupService;

/// <summary>
/// The agent's backup operations against the simulated server's memory: an artifact is a manifest
/// with a size and a digest derived from names, and no archive is written, read or uploaded.
/// </summary>
/// <remarks>
/// <para>
/// Only the local destination is served, exactly as the agent that ships today serves only that one:
/// a destination naming a bucket is refused with <c>AgentNotImplemented</c> and never falls back to
/// the local one, so the demo cannot be talked into holding a credential for a remote store. A
/// destination is never opened, whatever it names.
/// </para>
/// <para>
/// Both streams end in exactly one terminal event, and a cancelled caller sees the cancelled terminal
/// kind rather than an exception, as the real client's streams do. Each step yields control, so a
/// reader that stops reading or cancels is honoured between steps.
/// </para>
/// </remarks>
public sealed class SimulatedAgentBackupClient : IAgentBackupClient
{
    /// <summary>Percent complete reported while the home is being archived.</summary>
    private const uint ArchivingPercent = 30;

    /// <summary>Percent complete reported while the databases are being dumped.</summary>
    private const uint DumpingPercent = 70;

    /// <summary>Percent complete reported while the home is being restored.</summary>
    private const uint RestoringFilesPercent = 40;

    /// <summary>Percent complete reported while the databases are being reloaded.</summary>
    private const uint RestoringDatabasesPercent = 80;

    /// <summary>The progress a create stream reports before it ends: percent complete and the stage id.</summary>
    private static readonly (uint Percent, string Stage)[] CreateSteps =
    [
        (ArchivingPercent, "archiving_files"),
        (DumpingPercent, "dumping_databases"),
    ];

    /// <summary>The progress a restore stream reports before it ends: percent complete and the stage id.</summary>
    private static readonly (uint Percent, string Stage)[] RestoreSteps =
    [
        (RestoringFilesPercent, "restoring_files"),
        (RestoringDatabasesPercent, "restoring_databases"),
    ];

    /// <summary>The backup artifacts of the simulated server.</summary>
    private readonly SimulatedBackupStore _backups;

    /// <summary>The databases of the simulated server, which a backup dumps.</summary>
    private readonly SimulatedDatabaseStore _databases;

    /// <summary>The clock a backup's creation time is read from.</summary>
    private readonly IClock _clock;

    /// <summary>Creates a client over the shared simulated state.</summary>
    /// <param name="ledger">The simulated server's state.</param>
    /// <param name="clock">The clock a new backup is stamped with.</param>
    public SimulatedAgentBackupClient(SimulatedAgentLedger ledger, IClock clock)
    {
        _backups = ledger.Backups;
        _databases = ledger.Databases;
        _clock = clock;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<BackupCreateEvent> CreateAsync(
        string accountUsername,
        string backupId,
        AgentBackupDestination destination,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (destination is not AgentLocalBackupDestination)
        {
            yield return CreateEnded(BackupCreateEventKind.Failed, SimulatedAgentErrors.NotImplementedCode);
            yield break;
        }

        var existing = _backups.Find(accountUsername, backupId);
        if (existing is null)
        {
            foreach (var (percent, stage) in CreateSteps)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    yield return CreateEnded(BackupCreateEventKind.Cancelled, null);
                    yield break;
                }

                yield return new BackupCreateEvent(
                    BackupCreateEventKind.Progress,
                    percent,
                    stage,
                    0,
                    string.Empty,
                    0,
                    null);
                await Task.Yield();
            }
        }

        var stored = _backups.GetOrAdd(
            accountUsername,
            backupId,
            () =>
            {
                return new AgentBackupSummary(
                    backupId,
                    SimulatedBackupFigures.Describe(
                        accountUsername,
                        backupId,
                        _databases.ListFor(accountUsername),
                        _clock.UtcNow),
                    null);
            });

        if (!stored.IsSuccess)
        {
            yield return CreateEnded(BackupCreateEventKind.Failed, stored.Error!.Code);
            yield break;
        }

        var readable = stored.Value.Readable;
        yield return readable is null
            ? CreateEnded(BackupCreateEventKind.Failed, SimulatedAgentErrors.ValidationFailedCode)
            : new BackupCreateEvent(
                BackupCreateEventKind.Created,
                0,
                string.Empty,
                readable.ArtifactBytes,
                readable.ArtifactSha256,
                (ulong)readable.Manifest.Databases.Count,
                null);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<BackupRestoreEvent> RestoreAsync(
        string accountUsername,
        string backupId,
        AgentBackupDestination destination,
        string expectedSha256,
        IReadOnlyList<string> allowedDatabases,
        AgentRestoreScope scope,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var refusal = RefuseRestore(accountUsername, backupId, destination, expectedSha256, allowedDatabases, scope);
        if (refusal is not null)
        {
            yield return RestoreEnded(BackupRestoreEventKind.Failed, null, refusal);
            yield break;
        }

        foreach (var (percent, stage) in RestoreSteps)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                yield return RestoreEnded(BackupRestoreEventKind.Cancelled, null, null);
                yield break;
            }

            yield return new BackupRestoreEvent(BackupRestoreEventKind.Progress, percent, stage, null, null);
            await Task.Yield();
        }

        // Read again rather than assumed: the artifact may have been deleted while the stream was yielding.
        var restored = _backups.Find(accountUsername, backupId)?.Readable;
        if (restored is null)
        {
            yield return RestoreEnded(BackupRestoreEventKind.Failed, null, SimulatedAgentErrors.NotFound());
            yield break;
        }

        yield return RestoreEnded(BackupRestoreEventKind.Restored, OutcomeOf(scope, restored), null);
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<AgentBackupSummary>>> ListAsync(
        string accountUsername,
        AgentBackupDestination destination,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            destination is AgentLocalBackupDestination
                ? Result<IReadOnlyList<AgentBackupSummary>>.Ok(_backups.ListFor(accountUsername))
                : Result<IReadOnlyList<AgentBackupSummary>>.Fail(SimulatedAgentErrors.NotImplemented()));
    }

    /// <inheritdoc/>
    public Task<Result<AgentBackupVerification>> VerifyBackupAsync(
        string accountUsername,
        string backupId,
        AgentBackupDestination destination,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (destination is not AgentLocalBackupDestination)
        {
            return Task.FromResult(Result<AgentBackupVerification>.Fail(SimulatedAgentErrors.NotImplemented()));
        }

        var stored = _backups.Find(accountUsername, backupId);
        if (stored is null)
        {
            return Task.FromResult(Result<AgentBackupVerification>.Fail(SimulatedAgentErrors.NotFound()));
        }

        // A corrupt, unknown-version or not-a-regular-file artifact is a failure carrying a code,
        // never a success with empty counts.
        if (stored.Readable is null)
        {
            return Task.FromResult(Result<AgentBackupVerification>.Fail(SimulatedAgentErrors.ValidationFailed()));
        }

        var manifest = stored.Readable.Manifest;

        return Task.FromResult(
            Result<AgentBackupVerification>.Ok(
                new AgentBackupVerification(
                    SimulatedBackupFigures.MemberCount(manifest),
                    (uint)manifest.Databases.Count,
                    stored.Readable.ArtifactBytes)));
    }

    /// <inheritdoc/>
    public Task<Result> DeleteAsync(
        string accountUsername,
        string backupId,
        AgentBackupDestination destination,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            destination is AgentLocalBackupDestination
                ? _backups.Delete(accountUsername, backupId)
                : Result.Fail(SimulatedAgentErrors.NotImplemented()));
    }

    /// <inheritdoc/>
    public Task<Result<AgentPublicReadVerdict>> ProbeAsync(
        AgentBackupDestination destination,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The agent that ships today performs no probe for any destination, and the simulation is no
        // more willing to vouch for one: "not established" is the only honest answer, and the caller
        // must never read it as permission to save the destination.
        return Task.FromResult(Result<AgentPublicReadVerdict>.Fail(SimulatedAgentErrors.NotImplemented()));
    }

    /// <summary>Builds the one terminal event of a create stream that did not produce an artifact.</summary>
    /// <param name="kind">How the stream ended.</param>
    /// <param name="errorCode">The code of a failed stream; <c>null</c> otherwise.</param>
    /// <returns>The terminal event.</returns>
    private static BackupCreateEvent CreateEnded(BackupCreateEventKind kind, string? errorCode)
    {
        return new BackupCreateEvent(kind, 0, string.Empty, 0, string.Empty, 0, errorCode);
    }

    /// <summary>Builds the one terminal event of a restore stream.</summary>
    /// <param name="kind">How the stream ended.</param>
    /// <param name="outcome">How much was restored, on success.</param>
    /// <param name="failure">Why it failed, on failure.</param>
    /// <returns>The terminal event.</returns>
    private static BackupRestoreEvent RestoreEnded(
        BackupRestoreEventKind kind,
        AgentRestoreOutcome? outcome,
        Error? failure)
    {
        return new BackupRestoreEvent(kind, 0, string.Empty, outcome, failure);
    }

    /// <summary>Checks a restore the way the agent does, before it changes anything.</summary>
    /// <param name="accountUsername">System username of the account being restored into.</param>
    /// <param name="backupId">Identifier of the backup to restore.</param>
    /// <param name="destination">Where the artifact resides.</param>
    /// <param name="expectedSha256">The digest the panel recorded at creation.</param>
    /// <param name="allowedDatabases">Every database the panel still knows the account owns.</param>
    /// <param name="scope">What the restore replaces.</param>
    /// <returns>
    /// The refusal, or <c>null</c> when the restore may proceed. An empty digest is refused rather than
    /// read as "do not check"; a digest that differs, an unreadable artifact, or a manifest naming a
    /// database outside the allowed list is refused before any database is touched. A database restore
    /// is judged on the one database it names instead of the whole manifest.
    /// </returns>
    private Error? RefuseRestore(
        string accountUsername,
        string backupId,
        AgentBackupDestination destination,
        string expectedSha256,
        IReadOnlyList<string> allowedDatabases,
        AgentRestoreScope scope)
    {
        if (destination is not AgentLocalBackupDestination)
        {
            return SimulatedAgentErrors.NotImplemented();
        }

        if (string.IsNullOrEmpty(expectedSha256))
        {
            return SimulatedAgentErrors.InvalidInput();
        }

        var stored = _backups.Find(accountUsername, backupId);
        if (stored is null)
        {
            return SimulatedAgentErrors.NotFound();
        }

        if (stored.Readable is null || stored.Readable.ArtifactSha256 != expectedSha256)
        {
            return SimulatedAgentErrors.ValidationFailed();
        }

        if (scope is AgentDatabaseRestoreScope database)
        {
            // The real agent refuses a database the panel has forgotten before it reads the manifest,
            // and a known database the archive does not name as a part that is not in the backup.
            if (!allowedDatabases.Contains(database.DatabaseName))
            {
                return SimulatedAgentErrors.InvalidInput();
            }

            return stored.Readable.Manifest.Databases.Any(entry => { return entry.Name == database.DatabaseName; })
                ? null
                : SimulatedAgentErrors.NotFound();
        }

        if (scope is AgentSiteRestoreScope)
        {
            return null;
        }

        return stored.Readable.Manifest.Databases.Any(database => { return !allowedDatabases.Contains(database.Name); })
            ? SimulatedAgentErrors.ValidationFailed()
            : null;
    }

    /// <summary>States what a restore of <paramref name="scope"/> reports having replaced.</summary>
    /// <param name="scope">What the restore replaced.</param>
    /// <param name="restored">The backup it was restored from.</param>
    /// <returns>
    /// The whole account's files and every database for a whole restore; one database and no files for
    /// a database restore; one directory of files and no database for a site restore — the same figures
    /// the real agent states, so a screen built against the simulator reads them the same way.
    /// </returns>
    private static AgentRestoreOutcome OutcomeOf(AgentRestoreScope scope, AgentReadableBackup restored)
    {
        return scope switch
        {
            AgentDatabaseRestoreScope => new AgentRestoreOutcome(false, 1, 1),
            AgentSiteRestoreScope => new AgentRestoreOutcome(true, 0, 0),
            _ => new AgentRestoreOutcome(
                true, (uint)restored.Manifest.Databases.Count, (uint)restored.Manifest.Databases.Count),
        };
    }
}
