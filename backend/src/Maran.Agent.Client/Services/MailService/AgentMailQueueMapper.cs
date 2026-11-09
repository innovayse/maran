using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.MailService;

/// <summary>Reads the wire's queue listing into the panel's own.</summary>
internal static class AgentMailQueueMapper
{
    /// <summary>Projects the wire listing onto the panel's own.</summary>
    /// <param name="ok">The wire payload.</param>
    /// <returns>
    /// The listing, or <c>null</c> when an entry names no known queue or deferral class: an entry that
    /// cannot be read in full is refused, because showing it with a guessed queue would claim a fact the
    /// agent did not state.
    /// </returns>
    internal static AgentMailQueueListing? ToQueue(ListMailQueueOk ok)
    {
        var entries = new List<AgentMailQueueEntry>(ok.Entries.Count);
        foreach (var wire in ok.Entries)
        {
            var queue = ToQueueName(wire.Queue);
            var deferral = ToDeferralClass(wire.DeferralClass);
            if (queue is null || deferral is null)
            {
                return null;
            }

            entries.Add(new AgentMailQueueEntry(
                wire.QueueId,
                queue.Value,
                wire.Sender,
                wire.RecipientCount,
                wire.SizeBytes,
                DateTimeOffset.FromUnixTimeSeconds(wire.ArrivedAtUnix),
                deferral.Value));
        }

        return new AgentMailQueueListing(entries, ok.TotalMessages, ok.Truncated);
    }

    /// <summary>Projects the wire queue name onto the panel's own.</summary>
    /// <param name="queue">The wire value.</param>
    /// <returns>The panel's value, or <c>null</c> for the contract's zero value or one this build does not know.</returns>
    private static AgentMailQueueName? ToQueueName(MailQueueName queue)
    {
        return queue switch
        {
            MailQueueName.Active => AgentMailQueueName.Active,
            MailQueueName.Deferred => AgentMailQueueName.Deferred,
            MailQueueName.Held => AgentMailQueueName.Held,
            MailQueueName.Incoming => AgentMailQueueName.Incoming,
            _ => null,
        };
    }

    /// <summary>Projects the wire deferral class onto the panel's own.</summary>
    /// <param name="deferralClass">The wire value.</param>
    /// <returns>The panel's value, or <c>null</c> for the contract's zero value or one this build does not know.</returns>
    private static AgentMailDeferralClass? ToDeferralClass(MailDeferralClass deferralClass)
    {
        return deferralClass switch
        {
            MailDeferralClass.None => AgentMailDeferralClass.None,
            MailDeferralClass.Network => AgentMailDeferralClass.Network,
            MailDeferralClass.RemoteRefused => AgentMailDeferralClass.RemoteRefused,
            MailDeferralClass.LocalError => AgentMailDeferralClass.LocalError,
            MailDeferralClass.Other => AgentMailDeferralClass.Other,
            _ => null,
        };
    }
}
