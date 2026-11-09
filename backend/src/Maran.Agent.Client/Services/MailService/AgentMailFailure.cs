using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Resources;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;
using Microsoft.Extensions.Logging;

namespace Maran.Agent.Client.Services.MailService;

/// <summary>The failed results of a mail rpc: a translated wire failure, and an unreadable response.</summary>
/// <remarks>
/// Beside <see cref="AgentMailClient"/> rather than inside it so the one place a mail failure is logged
/// and redacted is small enough to read in full. Every wire error still goes through
/// <see cref="AgentErrorTranslator"/>; this type only fixes the success type of the result around it.
/// </remarks>
internal static class AgentMailFailure
{
    /// <summary>Translates a wire failure into a failed result, logging the agent's own words once.</summary>
    /// <typeparam name="T">The success type of the call.</typeparam>
    /// <param name="logger">Where the agent's diagnostic text goes; <see cref="Error"/> carries only a code.</param>
    /// <param name="error">The wire failure.</param>
    /// <param name="operation">The call that was refused, for the log line.</param>
    /// <param name="sentSecret">The secret this call carried, redacted from the log if quoted back.</param>
    /// <returns>A failed result carrying only a code.</returns>
    internal static Result<T> Translate<T>(
        ILogger logger,
        AgentError error,
        string operation,
        SensitiveString? sentSecret = null)
    {
        return Result<T>.Fail(AgentErrorTranslator.ToError(logger, error, operation, sentSecret));
    }

    /// <summary>The refusal of a response that carries neither branch, or one that cannot be read in full.</summary>
    /// <typeparam name="T">The success type of the call.</typeparam>
    /// <returns>A failed result carrying <c>AgentInvalidResponse</c>.</returns>
    internal static Result<T> Invalid<T>()
    {
        return Result<T>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure));
    }
}
