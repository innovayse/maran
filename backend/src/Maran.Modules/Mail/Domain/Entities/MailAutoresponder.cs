namespace Maran.Modules.Mail.Domain.Entities;

/// <summary>
/// The out-of-office reply of one mailbox. The row exists exactly while the mailbox has an
/// autoresponder; clearing it deletes the row, so there is no "disabled but kept" state to disagree
/// with the host.
/// </summary>
/// <remarks>
/// Scoped through its required mailbox. The text is the customer's own words and is written into a
/// Sieve script on the host, so the agent re-validates it against a grammar that excludes every
/// character the script language gives a meaning; the panel's validator applies the same limits first.
/// </remarks>
public sealed class MailAutoresponder
{
    /// <summary>The row's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The mailbox the reply belongs to; at most one autoresponder per mailbox.</summary>
    public Guid MailboxId { get; private set; }

    /// <summary>The mailbox, loaded when the reply is read through it.</summary>
    public Mailbox Mailbox { get; private set; } = null!;

    /// <summary>The subject line of the reply.</summary>
    public string Subject { get; private set; }

    /// <summary>The text of the reply.</summary>
    public string Body { get; private set; }

    /// <summary>The instant the reply starts being sent, or <c>null</c> for "from now".</summary>
    public DateTimeOffset? StartsAt { get; private set; }

    /// <summary>The instant the reply stops being sent, or <c>null</c> for "until cleared".</summary>
    public DateTimeOffset? EndsAt { get; private set; }

    /// <summary>The days between two replies to one sender, or <c>null</c> for the host's default.</summary>
    public int? ReplyIntervalDays { get; private set; }

    /// <summary>Records an autoresponder.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="mailboxId">The mailbox it belongs to.</param>
    /// <param name="subject">The reply's subject.</param>
    /// <param name="body">The reply's text.</param>
    /// <param name="startsAt">When it starts, or <c>null</c>.</param>
    /// <param name="endsAt">When it ends, or <c>null</c>.</param>
    /// <param name="replyIntervalDays">The days between replies to one sender, or <c>null</c>.</param>
    public MailAutoresponder(
        Guid id,
        Guid mailboxId,
        string subject,
        string body,
        DateTimeOffset? startsAt,
        DateTimeOffset? endsAt,
        int? replyIntervalDays)
    {
        Id = id;
        MailboxId = mailboxId;
        Subject = subject;
        Body = body;
        StartsAt = startsAt;
        EndsAt = endsAt;
        ReplyIntervalDays = replyIntervalDays;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private MailAutoresponder()
    {
        Subject = string.Empty;
        Body = string.Empty;
    }

    /// <summary>Replaces the whole reply.</summary>
    /// <param name="subject">The reply's subject.</param>
    /// <param name="body">The reply's text.</param>
    /// <param name="startsAt">When it starts, or <c>null</c>.</param>
    /// <param name="endsAt">When it ends, or <c>null</c>.</param>
    /// <param name="replyIntervalDays">The days between replies to one sender, or <c>null</c>.</param>
    public void Replace(
        string subject,
        string body,
        DateTimeOffset? startsAt,
        DateTimeOffset? endsAt,
        int? replyIntervalDays)
    {
        Subject = subject;
        Body = body;
        StartsAt = startsAt;
        EndsAt = endsAt;
        ReplyIntervalDays = replyIntervalDays;
    }
}
