using Maran.Modules.Mail.Domain.Enums;

namespace Maran.Modules.Mail.Common;

/// <summary>One message waiting in the mail queue.</summary>
/// <param name="QueueId">The mail transfer agent's id for the message; the handle to delete it by.</param>
/// <param name="Queue">The queue the message waits in.</param>
/// <param name="Sender">
/// The envelope sender. UNTRUSTED text: it came from whoever sent the message and is for the screen
/// to render as text and never as markup or a link.
/// </param>
/// <param name="RecipientCount">How many recipients the message still has.</param>
/// <param name="SizeBytes">The message size in bytes.</param>
/// <param name="ArrivedAt">When the message was accepted.</param>
/// <param name="DeferralClass">The class of reason the message waits, never the reason's own text.</param>
public sealed record MailQueueEntryDto(
    string QueueId,
    MailQueueName Queue,
    string Sender,
    uint RecipientCount,
    ulong SizeBytes,
    DateTimeOffset ArrivedAt,
    MailDeferralClass DeferralClass);
