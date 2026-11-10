using Maran.Agent.Client.Interfaces;
using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Services;

namespace Maran.Modules.Mail.Queries.GetMailQueueCount;

/// <summary>
/// Handles <see cref="GetMailQueueCountQuery"/>: the size of the queue, read cheaply and bounded
/// (issue #320).
/// </summary>
/// <remarks>
/// <para>
/// <b>Cheap and bounded.</b> The badge is polled once a minute by every administrator session, so the
/// agent is asked for a single entry: the answer carries the queue's total beside it, and the cost of
/// the call does not grow with the queue.
/// </para>
/// <para>
/// <b>A failed read is a failed request, never a count of zero.</b> A badge reading 0 on a host whose
/// queue could not be read would say "nothing is waiting" about something nobody looked at; the caller
/// draws no badge when this fails. A zero is the answer to a read that succeeded and found nothing, and
/// to one other case only: mail was never turned on (or has been turned off) by the administrator, so
/// there is no stack of ours to hold a queue and the agent is not asked at all.
/// </para>
/// </remarks>
public sealed class GetMailQueueCountQueryHandler
{
    /// <summary>The one entry asked for: the listing carries the total whatever the limit.</summary>
    private const uint SingleEntryLimit = 1;

    /// <summary>The agent, which owns the queue.</summary>
    private readonly IAgentMailClient _agent;

    /// <summary>Whether the administrator turned mail on.</summary>
    private readonly MailServerReader _server;

    /// <summary>Creates the handler.</summary>
    /// <param name="agent">The agent client that reads the queue.</param>
    /// <param name="server">Reads whether the administrator turned mail on.</param>
    public GetMailQueueCountQueryHandler(IAgentMailClient agent, MailServerReader server)
    {
        _agent = agent;
        _server = server;
    }

    /// <summary>Returns how many messages are queued.</summary>
    /// <param name="query">The (parameterless) read request.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The count, or the agent's own typed failure.</returns>
    public async Task<Result<MailQueueCountDto>> HandleAsync(
        GetMailQueueCountQuery query,
        CancellationToken cancellationToken)
    {
        var settings = await _server.FindEnabledAsync(cancellationToken);
        if (settings is null)
        {
            return Result<MailQueueCountDto>.Ok(new MailQueueCountDto(0));
        }

        var queue = await _agent.ListQueueAsync(SingleEntryLimit, cancellationToken);
        if (!queue.IsSuccess)
        {
            return Result<MailQueueCountDto>.Fail(queue.Error!);
        }

        return Result<MailQueueCountDto>.Ok(new MailQueueCountDto(queue.Value.TotalMessages));
    }
}
