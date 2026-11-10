using System.Runtime.CompilerServices;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.TransferService;
using Maran.Host.Tests.Fixtures;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;

namespace Maran.Host.Tests.Resilience;

/// <summary>An inner transfer client that counts its calls and can fail on demand.</summary>
internal sealed class RecordingAgentTransferClient : IAgentTransferClient
{
    /// <summary>How many unary calls fail with a transport error before one succeeds.</summary>
    public int FailuresBeforeSuccess { get; set; }

    /// <summary>When set, a unary call that gets past the transport faults answers this failure instead of a value.</summary>
    public SourceFailureReason? RefuseWith { get; set; }

    /// <summary>How many times a unary method was entered.</summary>
    public int Calls { get; private set; }

    /// <summary>How many times the stream method was entered and started producing.</summary>
    public int StreamStarts { get; private set; }

    /// <summary>Whether the stream method throws a transport error instead of yielding.</summary>
    public bool StreamFails { get; set; }

    /// <summary>The host of the last endpoint a call received.</summary>
    public string? LastHost { get; private set; }

    /// <inheritdoc/>
    public async Task<AgentSourceResult<AgentGeneratedSourceKey>> GenerateSourceKeyAsync(
        CancellationToken cancellationToken)
    {
        await EnterAsync();

        return Answer(new AgentGeneratedSourceKey("ssh-ed25519 AAAAx", new SensitiveString("KEY"), "SHA256:x"));
    }

    /// <inheritdoc/>
    public async Task<AgentSourceResult<IReadOnlyList<AgentScannedHostKey>>> ScanHostKeyAsync(
        AgentSourceEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        LastHost = endpoint.Host;
        await EnterAsync();

        return Answer<IReadOnlyList<AgentScannedHostKey>>([new AgentScannedHostKey("ssh-ed25519", "ssh-ed25519 AAAAx", "SHA256:x")]);
    }

    /// <inheritdoc/>
    public async Task<AgentSourceResult<AgentTestedSource>> TestSourceAsync(
        AgentSourceEndpoint endpoint,
        AgentSourceCredential credential,
        CancellationToken cancellationToken)
    {
        LastHost = endpoint.Host;
        await EnterAsync();

        return Answer(new AgentTestedSource(true, 1, 2, null, null, null, null, null, false));
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<AgentSourceInventoryEvent> ReadSourceInventoryAsync(
        AgentSourceEndpoint endpoint,
        AgentSourceCredential credential,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        StreamStarts++;
        LastHost = endpoint.Host;
        await Task.Yield();

        if (StreamFails)
        {
            throw AgentTransportFaults.ConnectRefused();
        }

        yield return new AgentSourceInventoryDone(0, 0);
    }

    /// <summary>Counts a unary call and fails it with a transport error while failures remain.</summary>
    /// <returns>A task that completes when the call is allowed to answer.</returns>
    private async Task EnterAsync()
    {
        Calls++;

        if (Calls <= FailuresBeforeSuccess)
        {
            throw AgentTransportFaults.ConnectRefused();
        }

        await Task.Yield();
    }

    /// <summary>Builds the answer of a call that reached the agent.</summary>
    /// <typeparam name="T">The value the call produces.</typeparam>
    /// <param name="value">The value of a success.</param>
    /// <returns>The value, or the refusal this client was told to answer.</returns>
    private AgentSourceResult<T> Answer<T>(T value)
    {
        return RefuseWith is { } reason
            ? AgentSourceResult.Fail<T>(Error.Of("AgentSystemFailure", ErrorType.Failure), reason)
            : AgentSourceResult.Ok<T>(value);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<AgentBackupExportEvent> ExportBackupAsync(
        string accountUsername,
        string backupId,
        Maran.SharedKernel.Security.SensitiveString signingKey,
        CancellationToken cancellationToken)
    {
        Calls++;

        return EmptyExport();
    }

    /// <inheritdoc/>
    public Task<AgentBackupImportResult> ImportBackupAsync(
        AgentBackupImportHeader header,
        IAsyncEnumerable<ReadOnlyMemory<byte>> chunks,
        CancellationToken cancellationToken)
    {
        Calls++;

        return Task.FromResult<AgentBackupImportResult>(
            new AgentBackupImported("backup", 1, new string('a', 64), 0, false));
    }

    /// <summary>An export that states its one ending and nothing else.</summary>
    /// <returns>The single ending.</returns>
    private static async IAsyncEnumerable<AgentBackupExportEvent> EmptyExport()
    {
        await Task.Yield();

        yield return new AgentBackupExportDone(0);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<AgentFilePullEvent> PullAccountFilesAsync(
        AgentFilePullRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        StreamStarts++;
        LastHost = request.Endpoint.Host;
        await Task.Yield();

        if (StreamFails)
        {
            throw AgentTransportFaults.ConnectRefused();
        }

        yield return new AgentFilePullDone(1, null);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<AgentDatabaseTransferEvent> StreamRemoteDatabaseAsync(
        AgentDatabaseTransferRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        StreamStarts++;
        LastHost = request.Endpoint.Host;
        await Task.Yield();

        if (StreamFails)
        {
            throw AgentTransportFaults.ConnectRefused();
        }

        yield return new AgentDatabaseTransferDone(0);
    }
}
