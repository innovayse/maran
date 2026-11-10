using System.Runtime.CompilerServices;
using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;
using Maran.SharedKernel.Security;
using Microsoft.Extensions.Logging;

namespace Maran.Agent.Client.Services.TransferService;

/// <summary>
/// Reads the agent's <c>ExportBackup</c> stream into typed events, with exactly one ending at the close.
/// </summary>
internal sealed class BackupExportStream
{
    /// <summary>The transport seam this stream drives; a stub in tests, a real gRPC call in production.</summary>
    private readonly ITransferServiceInvoker _invoker;

    /// <summary>Where the agent's own diagnostic text goes, since an error carries only a code.</summary>
    private readonly ILogger _logger;

    /// <summary>Creates a stream reader over a transport seam.</summary>
    /// <param name="invoker">The transport that performs the actual call.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public BackupExportStream(ITransferServiceInvoker invoker, ILogger logger)
    {
        _invoker = invoker;
        _logger = logger;
    }

    /// <summary>Streams the events the agent states, without guarding against the transport failing.</summary>
    /// <param name="accountUsername">System user name of the account.</param>
    /// <param name="backupId">Id of the published backup.</param>
    /// <param name="signingKey">The per-server signing key; held for the call only.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>The header, the chunks and exactly one ending.</returns>
    /// <exception cref="OperationCanceledException">The caller cancelled; that is not an ending the agent stated.</exception>
    public async IAsyncEnumerable<AgentBackupExportEvent> EventsAsync(
        string accountUsername,
        string backupId,
        SensitiveString signingKey,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = BackupTransferMapper.ToWire(accountUsername, backupId, signingKey);

        await foreach (var response in _invoker.ExportBackupAsync(request, cancellationToken))
        {
            var message = Translate(response, signingKey);
            yield return message;

            if (message is AgentBackupExportDone or AgentBackupExportFailed)
            {
                yield break;
            }
        }

        // A caller that stopped reading is not told the agent truncated the stream.
        cancellationToken.ThrowIfCancellationRequested();

        // The stream ended without the agent stating an outcome. Whatever was received is partial, and a
        // partial artifact accepted as a complete one is the harm this ending exists to prevent.
        yield return InvalidEnding();
    }

    /// <summary>Turns one wire message into the event it states.</summary>
    /// <param name="response">The wire message.</param>
    /// <param name="signingKey">The key this call carried, redacted from the log if the agent quotes it back.</param>
    /// <returns>The event; a message carrying no branch, or one that cannot be read in full, is a failure.</returns>
    private AgentBackupExportEvent Translate(ExportBackupResponse response, SensitiveString signingKey)
    {
        switch (response.ResultCase)
        {
            case ExportBackupResponse.ResultOneofCase.Header:
                return response.Header.Manifest.IsEmpty
                    || response.Header.Signature.IsEmpty
                    || response.Header.PublicKey.IsEmpty
                        ? InvalidEnding()
                        : new AgentBackupExportHeader(
                            response.Header.Manifest.ToByteArray(),
                            response.Header.Signature.ToByteArray(),
                            response.Header.PublicKey.ToByteArray());

            case ExportBackupResponse.ResultOneofCase.Chunk:
                return response.Chunk.Data.IsEmpty
                    ? InvalidEnding()
                    : new AgentBackupExportChunk(response.Chunk.Data.Memory);

            case ExportBackupResponse.ResultOneofCase.Done:
                return response.Done.ArtifactBytes > long.MaxValue
                    ? InvalidEnding()
                    : new AgentBackupExportDone((long)response.Done.ArtifactBytes);

            case ExportBackupResponse.ResultOneofCase.Error:
                return new AgentBackupExportFailed(
                    AgentErrorTranslator.ToError(
                        _logger,
                        response.Error,
                        nameof(IAgentTransferClient.ExportBackupAsync),
                        signingKey));

            default:
                return InvalidEnding();
        }
    }

    /// <summary>The ending for a message the agent was not entitled to send.</summary>
    /// <returns>A failure carrying <c>AgentInvalidResponse</c>.</returns>
    private static AgentBackupExportFailed InvalidEnding()
    {
        return new AgentBackupExportFailed(AgentSourceFailure.InvalidResponse());
    }
}
