using Maran.Agent.Client.Services.MailService;
using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Mail.Mappers;

/// <summary>Restates the agent's view of the mail queue as the wire shape of the queue screen.</summary>
/// <remarks>
/// A mapper translates and never decides. The sender is carried through untouched and stays
/// untrusted: it is the screen's duty to render it as text.
/// </remarks>
public static class MailQueueMapper
{
    /// <summary>Restates the messages of one queue listing.</summary>
    /// <param name="queue">What the agent listed.</param>
    /// <param name="displayNames">The module's localized names of things that are not failures.</param>
    /// <returns>The wire shape of each listed message, in the agent's order.</returns>
    public static IReadOnlyList<MailQueueEntryDto> ToEntries(
        AgentMailQueueListing queue,
        IStringLocalizer<DisplayNames> displayNames)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(displayNames);

        return queue.Entries
            .Select(entry =>
            {
                return ToEntryDto(entry, displayNames);
            })
            .ToList();
    }

    /// <summary>Restates one queued message.</summary>
    /// <param name="entry">The agent's entry.</param>
    /// <param name="displayNames">The module's localized names.</param>
    /// <returns>The wire shape.</returns>
    private static MailQueueEntryDto ToEntryDto(AgentMailQueueEntry entry, IStringLocalizer<DisplayNames> displayNames)
    {
        var queue = ToQueueName(entry.Queue);
        var deferral = ToDeferralClass(entry.DeferralClass);

        return new MailQueueEntryDto(
            entry.QueueId,
            queue,
            displayNames[QueueNameKey(queue)],
            entry.Sender,
            entry.RecipientCount,
            entry.SizeBytes,
            entry.ArrivedAt,
            deferral,
            displayNames[DeferralNameKey(deferral)]);
    }

    /// <summary>The resource key of a queue's localized name.</summary>
    /// <param name="queue">The queue.</param>
    /// <returns>The <c>DisplayNames</c> key that names it.</returns>
    private static string QueueNameKey(MailQueueName queue)
    {
        return queue switch
        {
            MailQueueName.Active => "MailQueueNameActive",
            MailQueueName.Deferred => "MailQueueNameDeferred",
            MailQueueName.Held => "MailQueueNameHeld",
            MailQueueName.Incoming => "MailQueueNameIncoming",
            _ => throw new ArgumentOutOfRangeException(nameof(queue), queue, "Undefined mail queue."),
        };
    }

    /// <summary>The resource key of a deferral class's localized name.</summary>
    /// <param name="deferral">The class.</param>
    /// <returns>The <c>DisplayNames</c> key that names it.</returns>
    private static string DeferralNameKey(MailDeferralClass deferral)
    {
        return deferral switch
        {
            MailDeferralClass.None => "MailDeferralNone",
            MailDeferralClass.Network => "MailDeferralNetwork",
            MailDeferralClass.RemoteRefused => "MailDeferralRemoteRefused",
            MailDeferralClass.LocalError => "MailDeferralLocalError",
            MailDeferralClass.Other => "MailDeferralOther",
            _ => throw new ArgumentOutOfRangeException(nameof(deferral), deferral, "Undefined deferral class."),
        };
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
