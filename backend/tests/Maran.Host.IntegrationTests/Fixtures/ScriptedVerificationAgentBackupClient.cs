using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.BackupService;
using Maran.SharedKernel.Results;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// Stands in for the agent's backup operations while the verification of a backup is driven over
/// real HTTP and real PostgreSQL: a run always completes, and the answer to a verification is
/// whatever the test scripted, recorded with the identifiers the panel asked about.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why only the agent is replaced.</b> The agent is a separate root process reading a root-owned
/// artifact, so it cannot be present in this host. What it does to an artifact — whether a flipped
/// byte is found — is the agent's own tests' and the polygon suite's to prove; what THIS double
/// states is the answer the panel must act on, so the panel's reaction (the stored state, the
/// journal, the mail) is observed end to end.
/// </para>
/// <para>
/// <b>It records what it was asked.</b> A verification that must not reach the agent (another
/// tenant's backup, a view-as refusal) is proved by <see cref="Verifications"/> staying empty, which
/// is the only way to see that nothing was asked.
/// </para>
/// </remarks>
public sealed class ScriptedVerificationAgentBackupClient : IAgentBackupClient
{
    /// <summary>The size a created artifact reports, so the panel's row is not written from zero.</summary>
    private const ulong SizeBytes = 4096;

    /// <summary>The digest a created artifact reports.</summary>
    private const string Sha256 = "0000000000000000000000000000000000000000000000000000000000000000";

    /// <summary>The lock guarding <see cref="_answer"/>, which the test thread sets and the host reads.</summary>
    private readonly object _gate = new();

    /// <summary>The answer the next verifications will be given.</summary>
    private Result<AgentBackupVerification> _answer = Result<AgentBackupVerification>.Ok(
        new AgentBackupVerification(12, 1, SizeBytes));

    /// <summary>Every create the panel asked for, in order.</summary>
    public List<(string AccountUsername, string BackupId)> Creates { get; } = [];

    /// <summary>Every verification the panel asked for, in order.</summary>
    public List<(string AccountUsername, string BackupId)> Verifications { get; } = [];

    /// <summary>How many verifications the panel has asked for so far.</summary>
    public int VerificationCount
    {
        get
        {
            lock (_gate)
            {
                return Verifications.Count;
            }
        }
    }

    /// <summary>Scripts every later verification to find a sound artifact.</summary>
    public void AnswerSound()
    {
        Script(Result<AgentBackupVerification>.Ok(new AgentBackupVerification(12, 1, SizeBytes)));
    }

    /// <summary>Scripts every later verification to fail with the given agent-derived code.</summary>
    /// <param name="code">The code the agent client would have translated the agent's error to.</param>
    /// <param name="type">The kind of that failure.</param>
    public void AnswerFailure(string code, ErrorType type)
    {
        Script(Result<AgentBackupVerification>.Fail(Error.Of(code, type)));
    }

    /// <summary>Records the create and replays a stream that states one completed outcome.</summary>
    /// <param name="accountUsername">The account being backed up.</param>
    /// <param name="backupId">The panel-assigned identifier of the backup.</param>
    /// <param name="destination">Where the artifact is written.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>One progress event and one terminal <c>Created</c> event.</returns>
    public async IAsyncEnumerable<BackupCreateEvent> CreateAsync(
        string accountUsername,
        string backupId,
        AgentBackupDestination destination,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Creates.Add((accountUsername, backupId));
        }

        await Task.CompletedTask;
        cancellationToken.ThrowIfCancellationRequested();

        yield return new BackupCreateEvent(
            BackupCreateEventKind.Progress, 50, "archiving_files", 0, string.Empty, 0, null);
        yield return new BackupCreateEvent(
            BackupCreateEventKind.Created, 100, string.Empty, SizeBytes, Sha256, 1, null);
    }

    /// <summary>Answers an empty stream; nothing in these tests restores.</summary>
    /// <param name="accountUsername">The account being restored into.</param>
    /// <param name="backupId">The backup to restore from.</param>
    /// <param name="destination">Where the artifact resides.</param>
    /// <param name="expectedSha256">The digest the panel recorded.</param>
    /// <param name="allowedDatabases">The databases the panel still knows the account owns.</param>
    /// <param name="scope">What the restore replaces.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>An empty stream, which no handler may read as a restore having happened.</returns>
    public async IAsyncEnumerable<BackupRestoreEvent> RestoreAsync(
        string accountUsername,
        string backupId,
        AgentBackupDestination destination,
        string expectedSha256,
        IReadOnlyList<string> allowedDatabases,
        AgentRestoreScope scope,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        cancellationToken.ThrowIfCancellationRequested();
        yield break;
    }

    /// <summary>Answers an empty listing; nothing in these tests lists.</summary>
    /// <param name="accountUsername">The account whose backups would be listed.</param>
    /// <param name="destination">The destination the listing is about.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>An empty listing.</returns>
    public Task<Result<IReadOnlyList<AgentBackupSummary>>> ListAsync(
        string accountUsername,
        AgentBackupDestination destination,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<IReadOnlyList<AgentBackupSummary>>.Ok([]));
    }

    /// <summary>Records the question and answers what the test scripted.</summary>
    /// <param name="accountUsername">System username of the account owning the backup.</param>
    /// <param name="backupId">Identifier of the backup to verify.</param>
    /// <param name="destination">Where the artifact currently resides.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The scripted answer.</returns>
    public Task<Result<AgentBackupVerification>> VerifyBackupAsync(
        string accountUsername,
        string backupId,
        AgentBackupDestination destination,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Verifications.Add((accountUsername, backupId));
            return Task.FromResult(_answer);
        }
    }

    /// <summary>Reports the artifact as deleted; nothing in these tests deletes.</summary>
    /// <param name="accountUsername">The account owning the backup.</param>
    /// <param name="backupId">The backup whose artifact would be removed.</param>
    /// <param name="destination">Where the artifact resides.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success.</returns>
    public Task<Result> DeleteAsync(
        string accountUsername,
        string backupId,
        AgentBackupDestination destination,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Ok());
    }

    /// <summary>Answers as the shipping agent does: no probe is performed at all.</summary>
    /// <param name="destination">The destination that would be probed.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>A not-implemented failure, which is never permission to save a destination.</returns>
    public Task<Result<AgentPublicReadVerdict>> ProbeAsync(
        AgentBackupDestination destination,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(
            Result<AgentPublicReadVerdict>.Fail(Error.Of("AgentNotImplemented", ErrorType.Unavailable)));
    }

    /// <summary>Replaces the scripted answer.</summary>
    /// <param name="answer">What later verifications are given.</param>
    private void Script(Result<AgentBackupVerification> answer)
    {
        lock (_gate)
        {
            _answer = answer;
        }
    }
}
