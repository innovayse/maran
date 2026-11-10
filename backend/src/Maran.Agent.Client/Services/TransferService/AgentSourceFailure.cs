using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Resources;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;
using Microsoft.Extensions.Logging;
using WireSourceFailureReason = Maran.Agent.V1.SourceFailureReason;

namespace Maran.Agent.Client.Services.TransferService;

/// <summary>The failed results of a source rpc: a translated wire failure, and an unreadable response.</summary>
/// <remarks>
/// Beside <see cref="AgentTransferClient"/> rather than inside it so the one place a source failure is
/// logged, redacted and given its reason is small enough to read in full. Every wire error still goes
/// through <see cref="AgentErrorTranslator"/>; this type fixes the success type of the result around it and
/// adds the reason the wire carries beside the error.
/// </remarks>
internal static class AgentSourceFailure
{
    /// <summary>Translates a wire failure into a failed result, logging the agent's own words once.</summary>
    /// <typeparam name="T">The success type of the call.</typeparam>
    /// <param name="logger">Where the agent's diagnostic text goes; <see cref="Error"/> carries only a code.</param>
    /// <param name="error">The wire failure.</param>
    /// <param name="reason">The reason the wire states beside the failure, or unspecified when the rpc has none.</param>
    /// <param name="operation">The call that was refused, for the log line.</param>
    /// <param name="sentSecret">The secret this call carried, redacted from the log if quoted back.</param>
    /// <returns>A failed result carrying only a code and the reason.</returns>
    internal static AgentSourceResult<T> Translate<T>(
        ILogger logger,
        AgentError error,
        WireSourceFailureReason reason,
        string operation,
        SensitiveString? sentSecret)
    {
        return AgentSourceResult.Fail<T>(
            AgentErrorTranslator.ToError(logger, error, operation, sentSecret),
            TransferMapper.ToReason(reason));
    }

    /// <summary>The refusal of a response that carries neither branch, or one that cannot be read in full.</summary>
    /// <typeparam name="T">The success type of the call.</typeparam>
    /// <returns>A failed result carrying <c>AgentInvalidResponse</c>.</returns>
    internal static AgentSourceResult<T> Invalid<T>()
    {
        return AgentSourceResult.Fail<T>(InvalidResponse());
    }

    /// <summary>The error of a response the agent was not entitled to send.</summary>
    /// <returns><c>AgentInvalidResponse</c>, a server-side failure kind.</returns>
    internal static Error InvalidResponse()
    {
        return Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure);
    }
}
