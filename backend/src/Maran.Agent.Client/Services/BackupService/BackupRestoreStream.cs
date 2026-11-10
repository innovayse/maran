using System.Runtime.CompilerServices;
using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Resources;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Maran.Agent.Client.Services.BackupService;

/// <summary>Reads the agent's <c>RestoreBackup</c> stream into typed restore events, one terminal event at the end.</summary>
internal sealed class BackupRestoreStream
{
    /// <summary>The transport seam this stream drives; a stub in tests, a real gRPC call in production.</summary>
    private readonly IBackupServiceInvoker _invoker;

    /// <summary>Where the agent's own diagnostic text goes, since an error carries only a code.</summary>
    private readonly ILogger _logger;

    /// <summary>Creates a stream reader over a transport seam.</summary>
    /// <param name="invoker">The transport that performs the actual call.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public BackupRestoreStream(IBackupServiceInvoker invoker, ILogger logger)
    {
        _invoker = invoker;
        _logger = logger;
    }

    /// <summary>Streams the restore events the agent states, without guarding against the transport failing.</summary>
    /// <param name="accountUsername">System username of the account being restored into.</param>
    /// <param name="backupId">Identifier of the backup to restore.</param>
    /// <param name="destination">Where the artifact currently resides.</param>
    /// <param name="expectedSha256">The digest the panel recorded when the artifact was created.</param>
    /// <param name="allowedDatabases">Every database the panel still knows this account owns.</param>
    /// <param name="scope">What the restore replaces.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>Progress events followed by exactly one terminal event.</returns>
    public async IAsyncEnumerable<BackupRestoreEvent> EventsAsync(
        string accountUsername,
        string backupId,
        AgentBackupDestination destination,
        string expectedSha256,
        IReadOnlyList<string> allowedDatabases,
        AgentRestoreScope scope,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = new RestoreBackupRequest
        {
            AccountUsername = accountUsername,
            BackupId = backupId,
            Destination = BackupDestinationMapper.ToWireDestination(destination, includeArchiveKey: true),
            ExpectedSha256 = expectedSha256,
            Scope = BackupDestinationMapper.ToWireScope(scope),
        };
        request.AllowedDatabases.AddRange(allowedDatabases);

        await foreach (var response in _invoker.RestoreBackupAsync(request, cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                yield return BackupStreamEvents.RestoreEnding(BackupRestoreEventKind.Cancelled);
                yield break;
            }

            if (response.ResultCase == RestoreBackupResponse.ResultOneofCase.Progress)
            {
                yield return new BackupRestoreEvent(
                    BackupRestoreEventKind.Progress,
                    response.Progress.Percent,
                    response.Progress.Stage,
                    null,
                    null);
                continue;
            }

            if (response.ResultCase == RestoreBackupResponse.ResultOneofCase.Ok)
            {
                yield return new BackupRestoreEvent(
                    BackupRestoreEventKind.Restored,
                    100,
                    string.Empty,
                    new AgentRestoreOutcome(
                        response.Ok.FilesRestored,
                        response.Ok.DatabasesRestored,
                        response.Ok.DatabasesTotal),
                    null);
                yield break;
            }

            if (response.ResultCase == RestoreBackupResponse.ResultOneofCase.Error)
            {
                yield return ToTerminalEvent(response.Error, destination);
                yield break;
            }

            yield return new BackupRestoreEvent(
                BackupRestoreEventKind.Failed,
                0,
                string.Empty,
                null,
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure));
            yield break;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            yield return BackupStreamEvents.RestoreEnding(BackupRestoreEventKind.Cancelled);
            yield break;
        }

        yield return BackupStreamEvents.RestoreEnding(BackupRestoreEventKind.Truncated);
    }

    /// <summary>Turns the terminal error of a restore stream into the event that ends the sequence.</summary>
    /// <param name="error">The failure payload that closed the stream.</param>
    /// <param name="destination">The destination this call named, for redacting its secret from the log.</param>
    /// <returns>The dropped or idle ending where the agent named one, and a typed failure otherwise.</returns>
    /// <remarks>
    /// The rolled-back and not-rolled-back database lists ride on this error's text. They are logged
    /// with it and not returned, because they are operator-facing detail naming a customer's database
    /// objects; the panel records the failure code and the operator reads the log line beside it.
    /// </remarks>
    private BackupRestoreEvent ToTerminalEvent(AgentError error, AgentBackupDestination destination)
    {
        if (error.Code == ErrorCode.StreamDropped)
        {
            return BackupStreamEvents.RestoreEnding(BackupRestoreEventKind.Dropped);
        }

        if (error.Code == ErrorCode.StreamIdle)
        {
            return BackupStreamEvents.RestoreEnding(BackupRestoreEventKind.Idle);
        }

        return new BackupRestoreEvent(
            BackupRestoreEventKind.Failed,
            0,
            string.Empty,
            null,
            AgentErrorTranslator.ToError(
                _logger,
                error,
                nameof(IAgentBackupClient.RestoreAsync),
                BackupDestinationMapper.SecretOf(destination)));
    }
}
