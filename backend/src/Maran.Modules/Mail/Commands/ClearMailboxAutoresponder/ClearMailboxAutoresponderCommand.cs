using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Mail.Commands.ClearMailboxAutoresponder;

/// <summary>Removes the automatic reply a mailbox sends. Clearing a mailbox that has none succeeds.</summary>
/// <param name="MailboxId">The mailbox, from the route. Established by the server; never bound from the body.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal. Established by the server.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal. Established by the server.</param>
public sealed record ClearMailboxAutoresponderCommand(
    [property: JsonIgnore][property: BindNever][BindNever] Guid MailboxId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
