using System.Runtime.CompilerServices;
using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;
using Maran.SharedKernel.Security;
using Microsoft.Extensions.Logging;

namespace Maran.Agent.Client.Services.TransferService;

/// <summary>
/// Reads the agent's <c>StreamRemoteDatabase</c> stream into typed events, held to the same standard as the
/// pull: exactly one terminal event, and a stream that ends without one is a failure.
/// </summary>
internal sealed class DatabaseTransferStream
{
    /// <summary>The transport seam.</summary>
    private readonly ITransferServiceInvoker _invoker;

    /// <summary>Where the agent's own diagnostic text goes.</summary>
    private readonly ILogger _logger;

    /// <summary>Creates the reader.</summary>
    /// <param name="invoker">The transport seam.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public DatabaseTransferStream(ITransferServiceInvoker invoker, ILogger logger)
    {
        _invoker = invoker;
        _logger = logger;
    }

    /// <summary>Starts the transfer and yields its events, ending after the first terminal one.</summary>
    /// <param name="request">The transfer.</param>
    /// <param name="cancellationToken">Cancels the stream; the agent runs the work to its end regardless.</param>
    /// <returns>The events.</returns>
    public async IAsyncEnumerable<AgentDatabaseTransferEvent> EventsAsync(
        AgentDatabaseTransferRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var wire = TransferRequestMapper.ToWire(request);
        var secret = TransferRequestMapper.SecretOf(request.Credential);

        await foreach (var response in _invoker.StreamRemoteDatabaseAsync(wire, cancellationToken))
        {
            var message = Translate(response, secret);

            yield return message;

            if (message is AgentDatabaseTransferDone or AgentDatabaseTransferFailed)
            {
                yield break;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        yield return InvalidEnding();
    }

    /// <summary>Reads one wire message.</summary>
    /// <param name="response">The wire message.</param>
    /// <param name="sentSecret">The key this call sent, so an agent that quotes it back cannot put it in the log.</param>
    /// <returns>The event.</returns>
    private AgentDatabaseTransferEvent Translate(StreamRemoteDatabaseResponse response, SensitiveString sentSecret)
    {
        switch (response.ResultCase)
        {
            case StreamRemoteDatabaseResponse.ResultOneofCase.Progress:
                var step = TransferMapper.ToDatabaseStep(response.Progress.Step);
                return step is null ? InvalidEnding() : new AgentDatabaseTransferProgress(step.Value);

            case StreamRemoteDatabaseResponse.ResultOneofCase.Done:
                return response.Done.DumpBytes > long.MaxValue
                    ? InvalidEnding()
                    : new AgentDatabaseTransferDone((long)response.Done.DumpBytes);

            case StreamRemoteDatabaseResponse.ResultOneofCase.Error:
                return new AgentDatabaseTransferFailed(
                    AgentErrorTranslator.ToError(
                        _logger,
                        response.Error,
                        nameof(IAgentTransferClient.StreamRemoteDatabaseAsync),
                        sentSecret),
                    TransferMapper.ToReason(response.FailureReason),
                    TransferMapper.ToTransferReason(response.TransferFailureReason));

            default:
                return InvalidEnding();
        }
    }

    /// <summary>The failed ending a stream gets when it did not state an outcome.</summary>
    /// <returns>The failed event.</returns>
    private static AgentDatabaseTransferFailed InvalidEnding()
    {
        return new AgentDatabaseTransferFailed(
            AgentSourceFailure.InvalidResponse(),
            SourceFailureReason.Unspecified,
            TransferFailureReason.Unspecified);
    }
}
