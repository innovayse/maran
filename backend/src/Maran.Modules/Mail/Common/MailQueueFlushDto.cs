namespace Maran.Modules.Mail.Common;

/// <summary>What a queue flush answers with.</summary>
/// <param name="QueuedMessages">The number of messages queued after the flush was requested, or null when it was not read.</param>
public sealed record MailQueueFlushDto(uint? QueuedMessages);
