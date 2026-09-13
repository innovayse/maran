using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.BackupService;
using Maran.SharedKernel.Results;

namespace Maran.Host.Tests.Resilience;

/// <summary>An inner backup client that counts its calls and can fail on demand.</summary>
internal sealed class RecordingAgentBackupClient : IAgentBackupClient
{
    /// <summary>How many calls fail with a transport error before one succeeds.</summary>
    public int FailuresBeforeSuccess { get; set; }

    /// <summary>How many times a unary method was entered.</summary>
    public int Calls { get; private set; }

    /// <summary>How many times a stream method was entered and started producing.</summary>
    public int StreamStarts { get; private set; }

    /// <summary>Whether the stream methods throw a transport error instead of yielding.</summary>
    public bool StreamsFail { get; set; }

    /// <summary>The backup id of the last stream request.</summary>
    public string? LastBackupId { get; private set; }

    /// <inheritdoc/>
    public async IAsyncEnumerable<BackupCreateEvent> CreateAsync(
        string accountUsername,
        string backupId,
        AgentBackupDestination destination,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        StreamStarts++;
        LastBackupId = backupId;
        await Task.Yield();

        if (StreamsFail)
        {
            throw new SocketException((int)SocketError.ConnectionRefused);
        }

        yield return new BackupCreateEvent(
            BackupCreateEventKind.Created,
            100,
            string.Empty,
            1,
            "deadbeef",
            0,
            null);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<BackupRestoreEvent> RestoreAsync(
        string accountUsername,
        string backupId,
        AgentBackupDestination destination,
        string expectedSha256,
        IReadOnlyList<string> allowedDatabases,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        StreamStarts++;
        LastBackupId = backupId;
        await Task.Yield();

        if (StreamsFail)
        {
            throw new SocketException((int)SocketError.ConnectionRefused);
        }

        yield return new BackupRestoreEvent(
            BackupRestoreEventKind.Restored,
            100,
            string.Empty,
            new AgentRestoreOutcome(true, 0, 0),
            null);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<AgentBackupSummary>>> ListAsync(
        string accountUsername,
        AgentBackupDestination destination,
        CancellationToken cancellationToken)
    {
        Calls++;

        if (Calls <= FailuresBeforeSuccess)
        {
            throw new SocketException((int)SocketError.ConnectionRefused);
        }

        await Task.Yield();

        return Result<IReadOnlyList<AgentBackupSummary>>.Ok([]);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> DeleteAsync(
        string accountUsername,
        string backupId,
        AgentBackupDestination destination,
        CancellationToken cancellationToken)
    {
        Calls++;

        if (Calls <= FailuresBeforeSuccess)
        {
            throw new SocketException((int)SocketError.ConnectionRefused);
        }

        await Task.Yield();

        return Result<bool>.Ok(true);
    }

    /// <inheritdoc/>
    public async Task<Result<AgentPublicReadVerdict>> ProbeAsync(
        AgentBackupDestination destination,
        CancellationToken cancellationToken)
    {
        Calls++;

        if (Calls <= FailuresBeforeSuccess)
        {
            throw new SocketException((int)SocketError.ConnectionRefused);
        }

        await Task.Yield();

        return Result<AgentPublicReadVerdict>.Ok(AgentPublicReadVerdict.Private);
    }
}
