namespace Maran.Modules.Mail.Commands.DeleteQueuedMessage;

/// <summary>Removes one message from the mail queue by its queue id.</summary>
/// <param name="QueueId">
/// The queue id from the route, built by the action and never bound from a body. Untrusted until the
/// validator has judged it: it is handed to the agent, which revalidates it.
/// </param>
/// <param name="IpAddress">The caller's address, for the journal.</param>
/// <param name="UserAgent">The caller's client, for the journal.</param>
public sealed record DeleteQueuedMessageCommand(string QueueId, string IpAddress, string UserAgent);
