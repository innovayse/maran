using Maran.Agent.Client.Interfaces;
using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Mail.Commands.FlushMailQueue;

/// <summary>
/// Handles <see cref="FlushMailQueueCommand"/>: asks the agent to retry every deferred message now
/// and journals the outcome.
/// </summary>
public sealed class FlushMailQueueCommandHandler
{
    /// <summary>The journal subject: a flush names no message, it concerns the whole queue.</summary>
    private const string Subject = "queue";

    /// <summary>The agent, which owns the queue.</summary>
    private readonly IAgentMailClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly MailAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="agent">The agent client that flushes the queue.</param>
    /// <param name="journal">This module's audit journal.</param>
    public FlushMailQueueCommandHandler(IAgentMailClient agent, MailAuditJournal journal)
    {
        _agent = agent;
        _journal = journal;
    }

    /// <summary>Requests the flush and answers with the queue size the agent read afterwards.</summary>
    /// <param name="command">The caller's address and client, for the journal.</param>
    /// <param name="cancellationToken">Cancels the agent call and the journal write.</param>
    /// <returns>The size of the queue after the request, or the agent's typed failure.</returns>
    public async Task<Result<MailQueueFlushDto>> HandleAsync(
        FlushMailQueueCommand command,
        CancellationToken cancellationToken)
    {
        var flushed = await _agent.FlushQueueAsync(cancellationToken);

        if (!flushed.IsSuccess)
        {
            await _journal.RecordFailureAsync(
                HostedMailAuditActions.MailQueueFlushed,
                Subject,
                command.IpAddress,
                command.UserAgent,
                cancellationToken);

            return Result<MailQueueFlushDto>.Fail(flushed.Error!);
        }

        await _journal.RecordSuccessAsync(
            HostedMailAuditActions.MailQueueFlushed,
            Subject,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<MailQueueFlushDto>.Ok(new MailQueueFlushDto(flushed.Value));
    }
}
