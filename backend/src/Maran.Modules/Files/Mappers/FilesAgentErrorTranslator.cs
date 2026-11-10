using Maran.Agent.Client.Errors;
using Maran.Modules.Files.Resources;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Files.Mappers;

/// <summary>
/// The single place an agent failure becomes something this module may carry: one of this module's
/// own error codes, plus one log line naming the agent's CODE and the subject the call was made against.
/// </summary>
/// <remarks>
/// <para>
/// An agent failure is re-coded here rather than passed through. A customer's path and file names can
/// appear in the agent's own sentence, and that sentence can name a location outside what the customer
/// may see; this module never forwards an agent detail. It maps the failure to a sentence it owns and
/// logs the code and the subject, which are enough to find the call without carrying what the agent
/// said. Nothing from the agent's text reaches a customer (rules/security.md item 8).
/// </para>
/// <para>
/// <b>One outcome is passed through unchanged: an agent that could not be reached.</b> It arrives as
/// <see cref="ErrorType.Unavailable"/> with a fixed sentence the panel owns, so re-coding it would only
/// turn a 503 "try again" into a 500 "something broke".
/// </para>
/// <para>
/// The agent codes are matched as string literals because <c>Maran.Agent.Client</c>'s generated
/// <c>ErrorMessages</c> class is internal to that project, so no module can name its members. That makes
/// this file the one place the coupling exists, and its behaviour is pinned by its own tests. Codes the
/// agent has no distinct name for — a destination that is not a folder, a destination inside its own
/// source — are produced by the handler that knows the call, not guessed from a code here.
/// </para>
/// </remarks>
public static class FilesAgentErrorTranslator
{
    /// <summary>Pre-compiled log delegate for a file call the agent refused.</summary>
    /// <remarks>
    /// It carries the operation, the subject and the agent's error CODE and nothing else: not the
    /// agent's sentence, and no file content.
    /// </remarks>
    private static readonly Action<ILogger, string, string, string, Exception?> LogAgentRefusal =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Warning,
            new EventId(1, nameof(FilesAgentErrorTranslator)),
            "Agent refused files {Operation} on {FilesSubject} with {AgentErrorCode}");

    /// <summary>Re-codes an agent failure as this module's own, logging the code and the subject.</summary>
    /// <param name="logger">Where the operator-facing breadcrumb goes.</param>
    /// <param name="agentError">The failure the agent client returned; only its code is read.</param>
    /// <param name="operation">Which call refused, so the log line names it.</param>
    /// <param name="subject">The account's system user name and the path, or another identifier; never content.</param>
    /// <returns>The error this module answers with, carrying one of its own codes.</returns>
    public static Error Translate(ILogger logger, Error agentError, string operation, string subject)
    {
        LogAgentRefusal(logger, operation, subject, agentError.Code, null);

        if (agentError.Type == ErrorType.Unavailable && agentError.Code == "AgentUnavailable")
        {
            return agentError;
        }

        return agentError.Code switch
        {
            AgentErrorCodes.NotFound => Error.Of(nameof(ErrorMessages.FilesEntryNotFound), ErrorType.NotFound),
            "AgentAlreadyExists" => Error.Of(nameof(ErrorMessages.FilesEntryExists), ErrorType.Conflict),
            "AgentQuotaExceeded" or "AgentBelowFreeSpaceFloor" =>
                Error.Of(nameof(ErrorMessages.FilesQuotaExceeded), ErrorType.Conflict),
            "AgentTooLarge" => Error.Of(nameof(ErrorMessages.FilesDownloadTooLarge), ErrorType.Validation),
            "AgentArchiveUnsafe" or "AgentLimitReached" =>
                Error.Of(nameof(ErrorMessages.FilesArchiveRefused), ErrorType.Validation),
            "AgentAccountSuspended" => Error.Of(nameof(ErrorMessages.FilesAccountSuspended), ErrorType.Conflict),
            "AgentSaturated" => Error.Of(nameof(ErrorMessages.FilesServerBusy), ErrorType.Unavailable),
            "AgentInvalidInput" => Error.Of(nameof(ErrorMessages.FilesPathInvalid), ErrorType.Validation),
            _ => Error.Of(nameof(ErrorMessages.FilesAgentFailed), ErrorType.Failure),
        };
    }
}
