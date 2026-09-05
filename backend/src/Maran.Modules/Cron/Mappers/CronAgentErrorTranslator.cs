using Maran.Modules.Cron.Resources;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Cron.Mappers;

/// <summary>
/// The single place an agent failure becomes something this module may carry: one of this module's
/// own error codes, plus one log line naming the agent's CODE and the identifier the call was made
/// against.
/// </summary>
/// <remarks>
/// <para>
/// <b>An agent failure is re-coded here rather than passed through, and that is a security rule
/// rather than a preference.</b> Every other module answers with the agent's own code
/// (<c>AgentNotFound</c>, <c>AgentSystemFailure</c>) and lets the agent's sentence go to the log the
/// client writes. For cron that is not safe enough: the panel's redaction knows how to remove key
/// material and secrets the PANEL minted, and a cron command is neither — it is the customer's own
/// text, it can legitimately carry a credential, and an agent diagnostic that quoted it back would
/// travel straight into an admin-readable log. So this module never forwards an agent detail
/// verbatim; it maps the failure to a sentence it owns, and logs the code and the entry id, which
/// are enough to find the entry without carrying what the entry runs.
/// </para>
/// <para>
/// Two agent outcomes carry meaning a customer needs and are mapped to their own codes: a missing
/// entry (404, and the same answer another tenant's entry gets — never a 403 that would confirm it
/// exists) and a duplicate the agent refused to install twice (409). Everything else — invalid
/// input the panel's own validators should already have caught, a validation failure, a system
/// failure, an answer the client could not read — collapses to one code, because the difference
/// between them is an operator's question and the log line is where an operator reads it.
/// </para>
/// <para>
/// The agent codes are matched as string literals because <c>Maran.Agent.Client</c>'s generated
/// <c>ErrorMessages</c> class is internal to that project, so no module can name its members. That
/// makes this file the one place the coupling exists rather than five, and the mapping's behaviour
/// is pinned by its own tests.
/// </para>
/// </remarks>
public static class CronAgentErrorTranslator
{
    /// <summary>The agent's code for an entry that is not in the crontab.</summary>
    private const string AgentNotFoundCode = "AgentNotFound";

    /// <summary>The agent's code for an entry it refused to install a second time.</summary>
    private const string AgentAlreadyExistsCode = "AgentAlreadyExists";

    /// <summary>
    /// Pre-compiled log delegate for a cron call the agent refused. Source-generated because an
    /// agent that is refusing fails every call at once.
    /// </summary>
    /// <remarks>
    /// It carries the operation, the identifier the call named and the agent's error CODE — and
    /// nothing else. In particular it carries neither the command nor the agent's own sentence:
    /// the first is the customer's possible credential, and the second is where the agent would
    /// quote it back.
    /// </remarks>
    private static readonly Action<ILogger, string, string, string, Exception?> LogAgentRefusal =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Warning,
            new EventId(1, nameof(CronAgentErrorTranslator)),
            "Agent refused cron {Operation} on {CronSubject} with {AgentErrorCode}");

    /// <summary>Re-codes an agent failure as this module's own, logging the code and the subject.</summary>
    /// <param name="logger">Where the operator-facing breadcrumb goes.</param>
    /// <param name="agentError">The failure the agent client returned; only its code is read.</param>
    /// <param name="operation">Which cron call refused, so the log line names it.</param>
    /// <param name="subject">
    /// The entry id the call named, or the account id for a call that names no entry. It MUST be an
    /// identifier: this value reaches a log line, and a caller passing a command here would put a
    /// customer's credential in it.
    /// </param>
    /// <returns>The error this module answers with, carrying one of its own codes.</returns>
    public static Error Translate(ILogger logger, Error agentError, string operation, string subject)
    {
        LogAgentRefusal(logger, operation, subject, agentError.Code, null);

        return ToError(agentError.Code);
    }

    /// <summary>Maps one agent error code onto this module's own, code and kind together.</summary>
    /// <param name="agentCode">The machine-stable code the agent client produced.</param>
    /// <returns>The error this module answers that outcome with.</returns>
    /// <remarks>
    /// The kind is chosen here rather than left to be inferred from the code's spelling, and the
    /// default arm is the reason it matters: an agent refusal this module has no specific answer for
    /// is the SERVER failing, not the caller sending something wrong. Inferred from the name,
    /// <c>CronOperationFailed</c> used to answer 400 and tell a customer to fix a request that was
    /// never at fault.
    /// </remarks>
    private static Error ToError(string agentCode)
    {
        return agentCode switch
        {
            AgentNotFoundCode => Error.Of(nameof(ErrorMessages.CronEntryNotFound), ErrorType.NotFound),
            AgentAlreadyExistsCode => Error.Of(nameof(ErrorMessages.CronEntryAlreadyExists), ErrorType.Conflict),
            _ => Error.Of(nameof(ErrorMessages.CronOperationFailed), ErrorType.Failure),
        };
    }
}
