using Maran.Modules.Mail.Domain.Entities;

namespace Maran.Modules.Mail.Common;

/// <summary>Outward view of a <see cref="MailAutoresponder"/>: the automatic reply one mailbox sends.</summary>
/// <param name="MailboxId">The mailbox that sends the reply; a mailbox has at most one.</param>
/// <param name="Subject">The subject line of the reply.</param>
/// <param name="Body">The text of the reply.</param>
/// <param name="StartsAt">When the reply begins to be sent, or <c>null</c> for "from now".</param>
/// <param name="EndsAt">When the reply stops being sent, or <c>null</c> for "until it is cleared".</param>
/// <param name="ReplyIntervalDays">
/// How many days pass before the same sender is answered again, or <c>null</c> for the mail host's default.
/// </param>
public sealed record MailAutoresponderDto(
    Guid MailboxId,
    string Subject,
    string Body,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    int? ReplyIntervalDays);
