using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.BackupService;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Backups.Tests.TestSupport;

/// <summary>
/// An <see cref="IAgentBackupClient"/> double: it replays a scripted create stream, answers a
/// scripted result for a delete, and records what it was asked for.
/// </summary>
/// <remarks>
/// <para>
/// It records rather than verifies. What this module owes is that it asks the agent for the right
/// account and the right backup id, and that it writes the panel's rows from the outcome the stream
/// stated; whether the agent's own wire mapping is right is pinned where that lives, in
/// <c>Maran.Agent.Client.Tests</c>.
/// </para>
/// <para>
/// The scripted stream is deliberately allowed to be anything the real client can produce —
/// including a sequence with no terminal event at all, which is the case a handler must not read as
/// a success and which no happy-path double could express.
/// </para>
/// </remarks>
public sealed class StubAgentBackupClient : IAgentBackupClient
{
    /// <summary>The events <see cref="CreateAsync"/> replays, in order.</summary>
    private readonly IReadOnlyList<BackupCreateEvent> _createEvents;

    /// <summary>The answer <see cref="DeleteAsync"/> gives.</summary>
    private readonly Result<bool> _deleteResult;

    /// <summary>The events <see cref="RestoreAsync"/> replays, in order.</summary>
    private readonly IReadOnlyList<BackupRestoreEvent> _restoreEvents;

    /// <summary>Creates the double.</summary>
    /// <param name="createEvents">The create stream to replay; empty for a stream that says nothing.</param>
    /// <param name="deleteResult">The delete answer; success when omitted.</param>
    /// <param name="restoreEvents">The restore stream to replay; empty for a stream that says nothing.</param>
    public StubAgentBackupClient(
        IReadOnlyList<BackupCreateEvent>? createEvents = null,
        Result<bool>? deleteResult = null,
        IReadOnlyList<BackupRestoreEvent>? restoreEvents = null)
    {
        _createEvents = createEvents ?? [];
        _deleteResult = deleteResult ?? Result<bool>.Ok(true);
        _restoreEvents = restoreEvents ?? [];
    }

    /// <summary>The account name and backup id of every create, in order.</summary>
    public List<(string AccountUsername, string BackupId)> Creates { get; } = [];

    /// <summary>The account name and backup id of every delete, in order.</summary>
    public List<(string AccountUsername, string BackupId)> Deletes { get; } = [];

    /// <summary>
    /// What every restore was asked for, in order: the account, the backup, the digest the panel
    /// expected, and the databases it was allowed to replace.
    /// </summary>
    /// <remarks>
    /// The last two are recorded because they are the two the panel DECIDES: the digest must be the
    /// panel's own recorded value and not something read from beside the artifact, and the allowed
    /// list must be the panel's rows and not the caller's. Neither is observable from the outcome, so
    /// a test that did not record them could not tell a correct restore from one aimed with the
    /// wrong list.
    /// </remarks>
    public List<(string AccountUsername, string BackupId, string ExpectedSha256, IReadOnlyList<string> AllowedDatabases)>
        Restores
    { get; } = [];

    /// <summary>The destination every call named, in order, so a test can assert where it was aimed.</summary>
    public List<AgentBackupDestination> Destinations { get; } = [];

    /// <summary>Run at the moment a create is asked for, before any event is replayed.</summary>
    /// <remarks>
    /// The only way to observe the panel's state DURING a run rather than after it. Whether the
    /// scheduled sweep stamps a schedule before or after the agent call is invisible once the sweep
    /// has returned — both orders leave the same row — and the whole question is what the next
    /// five-minute tick would see while the archive is still being written.
    /// </remarks>
    public Action? OnCreate { get; set; }

    /// <inheritdoc />
    public async IAsyncEnumerable<BackupCreateEvent> CreateAsync(
        string accountUsername,
        string backupId,
        AgentBackupDestination destination,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Creates.Add((accountUsername, backupId));
        Destinations.Add(destination);
        OnCreate?.Invoke();

        foreach (var scripted in _createEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return scripted;
        }

        await Task.CompletedTask;
    }

    /// <summary>Run at the moment a restore is asked for, before any event is replayed.</summary>
    /// <remarks>
    /// The restore's counterpart to <see cref="OnCreate"/>, and it exists for the same reason: the
    /// only way to reach the panel's state DURING a run. What it is used for is the one thing a
    /// scripted event sequence cannot express — the request going away mid-stream, which is what a
    /// closed browser tab and the shipped nginx's upstream read timeout both produce.
    /// </remarks>
    public Action? OnRestore { get; set; }

    /// <inheritdoc />
    /// <remarks>
    /// Replays whatever it was scripted with, including a sequence with no terminal event — which is
    /// the case a handler must never read as a completed restore and which no happy-path double
    /// could express.
    /// </remarks>
    public async IAsyncEnumerable<BackupRestoreEvent> RestoreAsync(
        string accountUsername,
        string backupId,
        AgentBackupDestination destination,
        string expectedSha256,
        IReadOnlyList<string> allowedDatabases,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Restores.Add((accountUsername, backupId, expectedSha256, allowedDatabases));
        Destinations.Add(destination);
        OnRestore?.Invoke();

        foreach (var scripted in _restoreEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return scripted;
        }

        await Task.CompletedTask;
    }

    /// <summary>What every listing was asked for, in order: the account, and the destination.</summary>
    /// <remarks>
    /// The panel's SCREENS list from its own rows — <c>ListBackupsQueryHandler</c> is constructed
    /// from the module's database context alone and holds no agent client, so it cannot reach this
    /// method at all. The one caller is <c>StartupBackupReconciler</c>, which asks the destination
    /// what artifacts exist rather than guessing about a row whose stream the panel lost. The calls
    /// are recorded because which account name and which destination the reclaim pass aims at is the
    /// thing it decides, and neither is visible in its outcome.
    /// </remarks>
    public List<(string AccountUsername, AgentBackupDestination Destination)> Listings { get; } = [];

    /// <summary>The answer <see cref="ListAsync"/> gives, or null to script a failure per test.</summary>
    /// <remarks>
    /// Settable rather than constructor-supplied because most tests here never list at all, and a
    /// required parameter would have to be threaded through every existing fixture. A test that does
    /// not set it gets an EMPTY successful listing — the destination holding nothing — which is a
    /// real state and the one the "no artifact" arm of the reclaim pass reads.
    /// </remarks>
    public Result<IReadOnlyList<AgentBackupSummary>>? ListResult { get; set; }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<AgentBackupSummary>>> ListAsync(
        string accountUsername,
        AgentBackupDestination destination,
        CancellationToken cancellationToken)
    {
        Listings.Add((accountUsername, destination));

        return Task.FromResult(
            ListResult ?? Result<IReadOnlyList<AgentBackupSummary>>.Ok([]));
    }

    /// <inheritdoc />
    public Task<Result<bool>> DeleteAsync(
        string accountUsername,
        string backupId,
        AgentBackupDestination destination,
        CancellationToken cancellationToken)
    {
        Deletes.Add((accountUsername, backupId));
        Destinations.Add(destination);
        return Task.FromResult(_deleteResult);
    }

    /// <inheritdoc />
    /// <remarks>Probing a destination is not this module's operation yet, for the reason above.</remarks>
    public Task<Result<AgentPublicReadVerdict>> ProbeAsync(
        AgentBackupDestination destination,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This module does not configure destinations yet.");
    }
}
