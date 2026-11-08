namespace Maran.Agent.Client.Services.MailService;

/// <summary>The queue a message waits in.</summary>
/// <remarks>
/// A panel-side mirror of the wire's <c>MailQueueName</c>. The wire's UNSPECIFIED has no member: an entry
/// that names no queue is refused as an invalid response.
/// </remarks>
public enum AgentMailQueueName
{
    /// <summary>Being delivered right now.</summary>
    Active = 1,

    /// <summary>Delivery failed temporarily and will be retried.</summary>
    Deferred = 2,

    /// <summary>Held by an operator or a policy and not retried.</summary>
    Held = 3,

    /// <summary>Accepted and not yet picked up for delivery.</summary>
    Incoming = 4,
}
