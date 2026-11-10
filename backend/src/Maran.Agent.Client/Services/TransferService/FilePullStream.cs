using System.Runtime.CompilerServices;
using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;
using Maran.SharedKernel.Security;
using Microsoft.Extensions.Logging;

namespace Maran.Agent.Client.Services.TransferService;

/// <summary>
/// Reads the agent's <c>PullAccountFiles</c> stream into typed events and holds it to the standard the other
/// streams are held to: exactly one terminal event, and a stream that ends without one is a failure.
/// </summary>
internal sealed class FilePullStream
{
    /// <summary>The transport seam.</summary>
    private readonly ITransferServiceInvoker _invoker;

    /// <summary>Where the agent's own diagnostic text goes.</summary>
    private readonly ILogger _logger;

    /// <summary>Creates the reader.</summary>
    /// <param name="invoker">The transport seam.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public FilePullStream(ITransferServiceInvoker invoker, ILogger logger)
    {
        _invoker = invoker;
        _logger = logger;
    }

    /// <summary>Starts the pull and yields its events, ending after the first terminal one.</summary>
    /// <param name="request">The pull.</param>
    /// <param name="cancellationToken">Cancels the stream; the agent stops at its next slice boundary.</param>
    /// <returns>The events.</returns>
    public async IAsyncEnumerable<AgentFilePullEvent> EventsAsync(
        AgentFilePullRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var wire = TransferRequestMapper.ToWire(request);
        var secret = TransferRequestMapper.SecretOf(request.Credential);

        await foreach (var response in _invoker.PullAccountFilesAsync(wire, cancellationToken))
        {
            var message = Translate(response, secret);

            yield return message;

            if (message is AgentFilePullDone or AgentFilePullFailed)
            {
                yield break;
            }
        }

        // A caller that stopped watching is not told the agent truncated the stream.
        cancellationToken.ThrowIfCancellationRequested();

        // Without a terminal message the copy is of unknown completeness; reading it as done would turn a
        // cut stream into a verified account.
        yield return InvalidEnding();
    }

    /// <summary>Reads one wire message.</summary>
    /// <param name="response">The wire message.</param>
    /// <param name="sentSecret">The key this call sent, so an agent that quotes it back cannot put it in the log.</param>
    /// <returns>The event.</returns>
    private AgentFilePullEvent Translate(PullAccountFilesResponse response, SensitiveString sentSecret)
    {
        switch (response.ResultCase)
        {
            case PullAccountFilesResponse.ResultOneofCase.Progress:
                return response.Progress.SlicesCompleted > int.MaxValue
                    ? InvalidEnding()
                    : new AgentFilePullProgress((int)response.Progress.SlicesCompleted);

            case PullAccountFilesResponse.ResultOneofCase.Done:
                if (response.Done.Slices > int.MaxValue
                    || (response.Done.SourceBytesKnown && response.Done.SourceBytes > long.MaxValue))
                {
                    return InvalidEnding();
                }

                return new AgentFilePullDone(
                    (int)response.Done.Slices,
                    response.Done.SourceBytesKnown ? (long)response.Done.SourceBytes : null);

            case PullAccountFilesResponse.ResultOneofCase.Error:
                return new AgentFilePullFailed(
                    AgentErrorTranslator.ToError(
                        _logger,
                        response.Error,
                        nameof(IAgentTransferClient.PullAccountFilesAsync),
                        sentSecret),
                    TransferMapper.ToReason(response.FailureReason),
                    TransferMapper.ToTransferReason(response.TransferFailureReason));

            default:
                return InvalidEnding();
        }
    }

    /// <summary>The failed ending a stream gets when it did not state an outcome.</summary>
    /// <returns>The failed event.</returns>
    private static AgentFilePullFailed InvalidEnding()
    {
        return new AgentFilePullFailed(
            AgentSourceFailure.InvalidResponse(),
            SourceFailureReason.Unspecified,
            TransferFailureReason.Unspecified);
    }
}
