using Maran.Agent.Client.Interfaces;
using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Mappers;

namespace Maran.Modules.Mail.Queries.ListMailQueue;

/// <summary>
/// Handles <see cref="ListMailQueueQuery"/>: reads the queue from the agent and restates it for the
/// wire.
/// </summary>
/// <remarks>
/// <para>
/// No limit is sent: zero asks for the agent's own default, which the agent also caps, so the size of
/// the answer is decided in one place. The answer says how many messages the queue holds in all and
/// whether the list is cut short of it.
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

    /// <summary>The agent, which owns the queue.</summary>
    private readonly IAgentMailClient _agent;

    /// <summary>Creates the handler.</summary>
    /// <param name="agent">The agent client that reads the queue.</param>
    public ListMailQueueQueryHandler(IAgentMailClient agent)
    {
        _agent = agent;
    }

    /// <summary>Lists the queue.</summary>
    /// <param name="query">The query; it carries nothing.</param>
    /// <param name="cancellationToken">Cancels the agent call.</param>
    /// <returns>The entries, the total and whether the list is truncated, or the agent's typed failure.</returns>
    public async Task<Result<MailQueueDto>> HandleAsync(ListMailQueueQuery query, CancellationToken cancellationToken)
    {
        var queue = await _agent.ListQueueAsync(AgentDefaultLimit, cancellationToken);
        if (!queue.IsSuccess)
        {
            return Result<MailQueueDto>.Fail(queue.Error!);
        }

        return Result<MailQueueDto>.Ok(MailQueueMapper.ToDto(queue.Value));
    }
}
