using Maran.Agent.Client.Services.MailService;
using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Domain.Enums;

namespace Maran.Modules.Mail.Mappers;

/// <summary>Restates the agent's view of the mail queue as the wire shape of the queue screen.</summary>
/// <remarks>
/// A mapper translates and never decides. The sender is carried through untouched and stays
/// untrusted: it is the screen's duty to render it as text.
/// </remarks>
public static class MailQueueMapper
{
    /// <summary>Restates one queue listing.</summary>
    /// <param name="queue">What the agent listed.</param>
    /// <returns>The wire shape.</returns>
    public static MailQueueDto ToDto(AgentMailQueueListing queue)
    {
        return new MailQueueDto(
            queue.Entries
                .Select(entry =>
                {
                    return ToEntryDto(entry);
                })
                .ToList(),
            queue.TotalMessages,
            queue.Truncated);
    }

    /// <summary>Restates one queued message.</summary>
    /// <param name="entry">The agent's entry.</param>
    /// <returns>The wire shape.</returns>
    private static MailQueueEntryDto ToEntryDto(AgentMailQueueEntry entry)
    {
        return new MailQueueEntryDto(
            entry.QueueId,
            ToQueueName(entry.Queue),
            entry.Sender,
            entry.RecipientCount,
            entry.SizeBytes,
            entry.ArrivedAt,
            ToDeferralClass(entry.DeferralClass));
    }

    /// <summary>Mirrors the agent's queue vocabulary.</summary>
    /// <param name="queue">The agent's value.</param>
    /// <returns>This module's value.</returns>
    private static MailQueueName ToQueueName(AgentMailQueueName queue)
    {
        return queue switch
        {
            AgentMailQueueName.Active => MailQueueName.Active,
            AgentMailQueueName.Deferred => MailQueueName.Deferred,
            AgentMailQueueName.Held => MailQueueName.Held,
            AgentMailQueueName.Incoming => MailQueueName.Incoming,
            _ => throw new ArgumentOutOfRangeException(nameof(queue), queue, "Unknown mail queue."),
        };
    }

    /// <summary>Mirrors the agent's deferral class vocabulary.</summary>
    /// <param name="deferral">The agent's value.</param>
    /// <returns>This module's value.</returns>
    private static MailDeferralClass ToDeferralClass(AgentMailDeferralClass deferral)
    {
        return deferral switch
        {
            AgentMailDeferralClass.None => MailDeferralClass.None,
            AgentMailDeferralClass.Network => MailDeferralClass.Network,
            AgentMailDeferralClass.RemoteRefused => MailDeferralClass.RemoteRefused,
            AgentMailDeferralClass.LocalError => MailDeferralClass.LocalError,
            AgentMailDeferralClass.Other => MailDeferralClass.Other,
            _ => throw new ArgumentOutOfRangeException(
                nameof(deferral),
                deferral,
                "Unknown mail deferral class."),
        };
    }
}
