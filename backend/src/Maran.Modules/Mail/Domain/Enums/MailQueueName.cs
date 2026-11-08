namespace Maran.Modules.Mail.Domain.Enums;

/// <summary>Which of the mail transfer agent's queues a message is waiting in.</summary>
public enum MailQueueName
{
    /// <summary>The message is being delivered right now.</summary>
    Active,

    /// <summary>A delivery attempt failed and the message waits for the next one.</summary>
    Deferred,

    /// <summary>An operator or a rule holds the message; nothing will try to deliver it.</summary>
    Held,

    /// <summary>The message was accepted and has not been picked up for delivery yet.</summary>
    Incoming,
}
