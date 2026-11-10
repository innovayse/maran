using System.Runtime.CompilerServices;
using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Resources;
using Maran.Agent.V1;
using Microsoft.Extensions.Logging;

namespace Maran.Agent.Client.Services.BackupService;

/// <summary>Reads the agent's <c>CreateBackup</c> stream into typed create events, one terminal event at the end.</summary>
internal sealed class BackupCreateStream
{
    /// <summary>The transport seam this stream drives; a stub in tests, a real gRPC call in production.</summary>
    private readonly IBackupServiceInvoker _invoker;

    /// <summary>Where the agent's own diagnostic text goes, since an error carries only a code.</summary>
    private readonly ILogger _logger;

    /// <summary>Creates a stream reader over a transport seam.</summary>
    /// <param name="invoker">The transport that performs the actual call.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public BackupCreateStream(IBackupServiceInvoker invoker, ILogger logger)
    {
        _invoker = invoker;
        _logger = logger;
    }

    /// <summary>Streams the create events the agent states, without guarding against the transport failing.</summary>
    /// <param name="accountUsername">System username of the account being backed up.</param>
    /// <param name="backupId">Caller-assigned identifier for this backup.</param>
    /// <param name="destination">Where to store the resulting artifact.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>Progress events followed by exactly one terminal event.</returns>
    public async IAsyncEnumerable<BackupCreateEvent> EventsAsync(
        string accountUsername,
        string backupId,
        AgentBackupDestination destination,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = new CreateBackupRequest
        {
            AccountUsername = accountUsername,
            BackupId = backupId,
            Destination = BackupDestinationMapper.ToWireDestination(destination, includeArchiveKey: true),
        };

        await foreach (var response in _invoker.CreateBackupAsync(request, cancellationToken))
        {
            // Checked here rather than left to the transport, so a caller that stopped watching gets
            // "you stopped watching" and not "the outcome was never reported".
            if (cancellationToken.IsCancellationRequested)
            {
                yield return BackupStreamEvents.CreateEnding(BackupCreateEventKind.Cancelled);
                yield break;
            }

            if (response.ResultCase == CreateBackupResponse.ResultOneofCase.Progress)
            {
                yield return new BackupCreateEvent(
                    BackupCreateEventKind.Progress,
                    response.Progress.Percent,
                    response.Progress.Stage,
                    0,
                    string.Empty,
                    0,
                    null);
                continue;
            }

            if (response.ResultCase == CreateBackupResponse.ResultOneofCase.Ok)
            {
                // The contract (backup.proto, CreateBackupOk.encrypted) obliges a caller that sent a key and
                // reads "not sealed" to treat the creation as failed: that agent predates sealing and left a
                // plain archive on disk where a sealed one was asked for. Never recorded as a completed backup.
                if (destination is AgentLocalBackupDestination { ArchiveKey: not null } && !response.Ok.Encrypted)
                {
                    yield return BackupStreamEvents.CreateFailed(nameof(ErrorMessages.AgentInvalidResponse));
                    yield break;
                }

                yield return new BackupCreateEvent(
                    BackupCreateEventKind.Created,
                    100,
                    string.Empty,
                    response.Ok.SizeBytes,
                    response.Ok.Sha256,
                    response.Ok.DatabaseCount,
                    null);
                yield break;
            }

            if (response.ResultCase == CreateBackupResponse.ResultOneofCase.Error)
            {
                yield return ToTerminalEvent(response.Error, destination);
                yield break;
            }

            // A message carrying no branch at all is neither progress nor an outcome.
            yield return BackupStreamEvents.CreateFailed(nameof(ErrorMessages.AgentInvalidResponse));
            yield break;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            yield return BackupStreamEvents.CreateEnding(BackupCreateEventKind.Cancelled);
            yield break;
        }

        // The stream ended without the agent stating an outcome. There may or may not be an artifact
        // on the destination, so this is reported as a truncation and never as a completion.
        yield return BackupStreamEvents.CreateEnding(BackupCreateEventKind.Truncated);
    }

    /// <summary>Turns the terminal error of a create stream into the event that ends the sequence.</summary>
    /// <param name="error">The failure payload that closed the stream.</param>
    /// <param name="destination">The destination this call named, for redacting its secret from the log.</param>
    /// <returns>The dropped or idle ending where the agent named one, and a typed failure otherwise.</returns>
    private BackupCreateEvent ToTerminalEvent(AgentError error, AgentBackupDestination destination)
    {
        if (error.Code == ErrorCode.StreamDropped)
        {
            return BackupStreamEvents.CreateEnding(BackupCreateEventKind.Dropped);
        }

        if (error.Code == ErrorCode.StreamIdle)
        {
            return BackupStreamEvents.CreateEnding(BackupCreateEventKind.Idle);
        }

        return BackupStreamEvents.CreateFailed(
            AgentErrorTranslator.ToError(
                _logger,
                error,
                nameof(IAgentBackupClient.CreateAsync),
                BackupDestinationMapper.SecretOf(destination)).Code);
    }
}
