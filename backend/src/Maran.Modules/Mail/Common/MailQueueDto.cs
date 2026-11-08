namespace Maran.Modules.Mail.Common;

/// <summary>The host's mail queue, bounded.</summary>
/// <param name="Entries">The queued messages the agent returned.</param>
/// <param name="Total">How many messages the queue holds in all.</param>
/// <param name="Truncated">Whether <paramref name="Entries"/> is cut short of <paramref name="Total"/>.</param>
public sealed record MailQueueDto(IReadOnlyList<MailQueueEntryDto> Entries, uint Total, bool Truncated);
