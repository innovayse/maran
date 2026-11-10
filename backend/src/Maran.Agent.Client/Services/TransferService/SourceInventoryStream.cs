using System.Runtime.CompilerServices;
using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;
using Maran.SharedKernel.Security;
using Microsoft.Extensions.Logging;

namespace Maran.Agent.Client.Services.TransferService;

/// <summary>
/// Reads the agent's <c>ReadSourceInventory</c> stream into typed events, with exactly one ending at the
/// close.
/// </summary>
internal sealed class SourceInventoryStream
{
    /// <summary>The transport seam this stream drives; a stub in tests, a real gRPC call in production.</summary>
    private readonly ITransferServiceInvoker _invoker;

    /// <summary>Where the agent's own diagnostic text goes, since an error carries only a code.</summary>
    private readonly ILogger _logger;

    /// <summary>Creates a stream reader over a transport seam.</summary>
    /// <param name="invoker">The transport that performs the actual call.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public SourceInventoryStream(ITransferServiceInvoker invoker, ILogger logger)
    {
        _invoker = invoker;
        _logger = logger;
    }

    /// <summary>Streams the events the agent states, without guarding against the transport failing.</summary>
    /// <param name="endpoint">The source.</param>
    /// <param name="credential">The key to log in with and the host key to require.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>Progress, account and warning events followed by exactly one ending.</returns>
    /// <exception cref="OperationCanceledException">The caller cancelled; that is not an ending the agent stated.</exception>
    public async IAsyncEnumerable<AgentSourceInventoryEvent> EventsAsync(
        AgentSourceEndpoint endpoint,
        AgentSourceCredential credential,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = new ReadSourceInventoryRequest
        {
            Endpoint = TransferRequestMapper.ToWire(endpoint),
            Credential = TransferRequestMapper.ToWire(credential),
        };

        await foreach (var response in _invoker.ReadSourceInventoryAsync(request, cancellationToken))
        {
            var message = Translate(response, TransferRequestMapper.SecretOf(credential));

            yield return message;

            if (message is AgentSourceInventoryDone or AgentSourceInventoryFailed)
            {
                yield break;
            }
        }

        // A caller that stopped watching is not told the agent truncated the stream.
        cancellationToken.ThrowIfCancellationRequested();

        // The stream ended without the agent stating an outcome. Whatever was received is partial, and a
        // partial inventory recorded as a complete one is the harm this ending exists to prevent.
        yield return InvalidEnding();
    }

    /// <summary>Turns one wire message into the event it states.</summary>
    /// <param name="response">The wire message.</param>
    /// <param name="sentSecret">The private key this call carried, redacted from the log if the agent quotes it back.</param>
    /// <returns>
    /// The event; a message carrying no branch, or one that cannot be read in full, is a failure and never
    /// something to skip.
    /// </returns>
    private AgentSourceInventoryEvent Translate(
        ReadSourceInventoryResponse response,
        SensitiveString sentSecret)
    {
        switch (response.ResultCase)
        {
            case ReadSourceInventoryResponse.ResultOneofCase.Progress:
                var step = TransferMapper.ToStep(response.Progress.Step);
                return step is null ? InvalidEnding() : new AgentSourceInventoryProgress(step.Value);

            case ReadSourceInventoryResponse.ResultOneofCase.Account:
                var account = TransferMapper.ToAccount(response.Account);
                return account is null ? InvalidEnding() : new AgentSourceInventoryAccountFound(account);

            case ReadSourceInventoryResponse.ResultOneofCase.Warning:
                var warning = TransferMapper.ToWarning(response.Warning);
                return warning is null ? InvalidEnding() : new AgentSourceInventoryWarningRaised(warning);

            case ReadSourceInventoryResponse.ResultOneofCase.Done:
                return response.Done.AccountCount > int.MaxValue || response.Done.WarningCount > int.MaxValue
                    ? InvalidEnding()
                    : new AgentSourceInventoryDone((int)response.Done.AccountCount, (int)response.Done.WarningCount);

            case ReadSourceInventoryResponse.ResultOneofCase.Error:
                return new AgentSourceInventoryFailed(
                    AgentErrorTranslator.ToError(
                        _logger,
                        response.Error,
                        nameof(IAgentTransferClient.ReadSourceInventoryAsync),
                        sentSecret),
                    TransferMapper.ToReason(response.FailureReason));

            default:
                return InvalidEnding();
        }
    }

    /// <summary>The ending for a message the agent was not entitled to send.</summary>
    /// <returns>A failure carrying <c>AgentInvalidResponse</c>.</returns>
    private static AgentSourceInventoryFailed InvalidEnding()
    {
        return new AgentSourceInventoryFailed(AgentSourceFailure.InvalidResponse(), SourceFailureReason.Unspecified);
    }
}
