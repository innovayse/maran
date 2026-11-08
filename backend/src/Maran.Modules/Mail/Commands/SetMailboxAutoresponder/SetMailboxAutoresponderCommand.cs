using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Mail.Commands.SetMailboxAutoresponder;

/// <summary>
/// Sets the automatic reply a mailbox sends, replacing the one it had. A mailbox has at most one; clearing
/// it is a separate operation.
/// </summary>
/// <param name="Subject">The subject line of the reply: at most 200 characters, no control characters.</param>
/// <param name="Body">
/// The text of the reply: at most 4096 bytes of UTF-8, line feeds as its only control character, no line
/// beginning with a full stop and no <c>${</c>, because the host renders it into a mail-filtering script.
/// </param>
/// <param name="StartsAt">When the reply begins to be sent, or <c>null</c> for "from now".</param>
/// <param name="EndsAt">
/// When the reply stops being sent, or <c>null</c> for "until it is cleared"; must be after the start.
/// </param>
/// <param name="ReplyIntervalDays">
/// How many days pass before the same sender is answered again, 1 to 30, or <c>null</c> for the host's
/// default.
/// </param>
/// <param name="MailboxId">The mailbox, from the route. Established by the server; never bound from the body.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal. Established by the server.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal. Established by the server.</param>
public sealed record SetMailboxAutoresponderCommand(
    string Subject,
    string Body,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    int? ReplyIntervalDays,
    [property: JsonIgnore][property: BindNever][BindNever] Guid MailboxId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
