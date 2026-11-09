namespace Maran.Agent.Client.Services.MailService;

/// <summary>One queued message.</summary>
/// <param name="QueueId">The MTA's queue id, letters and digits only.</param>
/// <param name="Queue">The queue the message waits in.</param>
/// <param name="Sender">
/// The envelope sender. UNTRUSTED TEXT from the network: a screen escapes it and the panel never builds
/// a path or a command from it.
/// </param>
/// <param name="RecipientCount">How many recipients are still undelivered.</param>
/// <param name="SizeBytes">The message size in bytes.</param>
/// <param name="ArrivedAt">When the MTA accepted the message.</param>
/// <param name="DeferralClass">Why the message is deferred, as a closed class.</param>
public sealed record AgentMailQueueEntry(
    string QueueId,
    AgentMailQueueName Queue,
    string Sender,
    uint RecipientCount,
    ulong SizeBytes,
    DateTimeOffset ArrivedAt,
    AgentMailDeferralClass DeferralClass);
