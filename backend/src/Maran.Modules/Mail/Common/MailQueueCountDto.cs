namespace Maran.Modules.Mail.Common;

/// <summary>How many messages the host's mail queue holds, the figure behind the navigation badge.</summary>
/// <param name="Count">The number of queued messages, across every queue the mail transfer agent keeps.</param>
public sealed record MailQueueCountDto(uint Count);
