using Maran.Agent.Client.Interfaces;
using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Mappers;
using Maran.Modules.Mail.Resources;
using Maran.SharedKernel.Extensions;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Mail.Queries.ListMailQueue;

/// <summary>
/// Handles <see cref="ListMailQueueQuery"/>: reads the queue from the agent, restates it for the wire
/// and pages it in memory.
/// </summary>
/// <remarks>
/// <para>
/// No limit is sent: zero asks for the agent's own default, which the agent also caps, so the size of
/// the answer is decided in one place. The answer says how many messages the queue holds in all and
/// whether the list is cut short of it. The agent has no page parameter, so the search, the order and
/// the page are applied here, over what it listed, by the pager the rest of the panel uses for an agent
/// list.
/// </para>
/// <para>
/// A sender is untrusted text, read from a message anybody on the internet could have sent. It is
/// returned as data for the screen to render as text, and is never parsed, logged or journaled here.
/// </para>
/// </remarks>
public sealed class ListMailQueueQueryHandler
{
    /// <summary>Zero, the agent's spelling of "use your default limit".</summary>
    private const uint AgentDefaultLimit = 0;

    /// <summary>What a search reads: the queue id and the sender.</summary>
    private static readonly IReadOnlyList<Func<MailQueueEntryDto, string>> SearchFields =
    [
        entry =>
        {
            return entry.QueueId;
        },
        entry =>
        {
            return entry.Sender;
        },
    ];

    /// <summary>The agent, which owns the queue.</summary>
    private readonly IAgentMailClient _agent;

    /// <summary>The module's localized names, for each row's queue and deferral class.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="agent">The agent client that reads the queue.</param>
    /// <param name="displayNames">The module's localized names.</param>
    public ListMailQueueQueryHandler(IAgentMailClient agent, IStringLocalizer<DisplayNames> displayNames)
    {
        _agent = agent;
        _displayNames = displayNames;
    }

    /// <summary>Lists the queue.</summary>
    /// <param name="query">The validated list request.</param>
    /// <param name="cancellationToken">Cancels the agent call.</param>
    /// <returns>The page, the queue's total and whether the list is truncated, or the agent's typed failure.</returns>
    public async Task<Result<MailQueueDto>> HandleAsync(ListMailQueueQuery query, CancellationToken cancellationToken)
    {
        var queue = await _agent.ListQueueAsync(AgentDefaultLimit, cancellationToken);
        if (!queue.IsSuccess)
        {
            return Result<MailQueueDto>.Fail(queue.Error!);
        }

        var page = MailQueueMapper.ToEntries(queue.Value, _displayNames)
            .ToPaged(query, ListMailQueueSorts.Map, SearchFields);

        return Result<MailQueueDto>.Ok(new MailQueueDto(page, queue.Value.TotalMessages, queue.Value.Truncated));
    }
}
