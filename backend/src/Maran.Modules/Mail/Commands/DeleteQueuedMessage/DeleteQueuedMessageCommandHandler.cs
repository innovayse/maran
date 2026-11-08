using Maran.Agent.Client.Interfaces;
using Maran.Modules.Mail.Resources;
using Maran.Modules.Mail.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Mail.Commands.DeleteQueuedMessage;

/// <summary>
/// Handles <see cref="DeleteQueuedMessageCommand"/>: asks the agent to remove one queued message and
/// journals the outcome.
/// </summary>
/// <remarks>
/// The agent's NOT_FOUND has one meaning on this call, that no such message is queued, and it
/// becomes <c>MailQueueEntryNotFound</c>; every other failure passes through. The id is journaled as
/// the subject: the validator has already limited it to letters and digits, so it can carry no
/// control character into the journal.
/// </remarks>
public sealed class DeleteQueuedMessageCommandHandler
{
    /// <summary>The stable code <c>AgentErrorTranslator</c> produces for the agent's NOT_FOUND.</summary>
    /// <remarks>
    /// A private constant and not the agent client's generated resource class, which is that
    /// project's internal detail; the string is the contract between the two projects.
    /// </remarks>
    private const string AgentNotFoundCode = "AgentNotFound";

    /// <summary>The agent, which owns the queue.</summary>
    private readonly IAgentMailClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly MailAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="agent">The agent client that removes the message.</param>
    /// <param name="journal">This module's audit journal.</param>
    public DeleteQueuedMessageCommandHandler(IAgentMailClient agent, MailAuditJournal journal)
    {
        _agent = agent;
        _journal = journal;
    }

    /// <summary>Removes the queued message.</summary>
    /// <param name="command">The validated queue id and the caller's address and client.</param>
    /// <param name="cancellationToken">Cancels the agent call and the journal write.</param>
    /// <returns>Success, <c>MailQueueEntryNotFound</c>, or the agent's typed failure.</returns>
    public async Task<Result> HandleAsync(DeleteQueuedMessageCommand command, CancellationToken cancellationToken)
    {
        var deleted = await _agent.DeleteQueuedMessageAsync(command.QueueId, cancellationToken);

        if (!deleted.IsSuccess)
        {
            await _journal.RecordFailureAsync(
                HostedMailAuditActions.MailQueueMessageDeleted,
                command.QueueId,
                command.IpAddress,
                command.UserAgent,
                cancellationToken);

            return Result.Fail(Translate(deleted.Error!));
        }

        await _journal.RecordSuccessAsync(
            HostedMailAuditActions.MailQueueMessageDeleted,
            command.QueueId,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result.Ok();
    }

    /// <summary>Names the agent's NOT_FOUND as this module's own code and passes every other failure through.</summary>
    /// <param name="error">The typed failure the agent client produced.</param>
    /// <returns>The error the caller is answered with.</returns>
    private static Error Translate(Error error)
    {
        if (string.Equals(error.Code, AgentNotFoundCode, StringComparison.Ordinal))
        {
            return Error.Of(nameof(ErrorMessages.MailQueueEntryNotFound), ErrorType.NotFound);
        }

        return error;
    }
}
