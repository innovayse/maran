namespace Maran.Modules.Mail.Common;

/// <summary>One page of the host's mail queue, beside the size of the queue itself.</summary>
/// <param name="Messages">
/// The requested page of the messages the agent listed, narrowed by the search; its own total counts the
/// listed messages that matched.
/// </param>
/// <param name="QueueTotal">How many messages the queue holds in all, listed or not.</param>
/// <param name="Truncated">
/// Whether the agent listed fewer messages than <paramref name="QueueTotal"/>, so the pages and the search
/// cover the newest of them only.
/// </param>
public sealed record MailQueueDto(PagedResult<MailQueueEntryDto> Messages, uint QueueTotal, bool Truncated);
