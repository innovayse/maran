using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Mail.Commands.UpdateMailbox;

/// <summary>
/// Changes a mailbox's size limit, its hourly send limit and whether its owner has it switched on. The
/// address and the password change through other operations.
/// </summary>
/// <param name="QuotaMib">The new size limit, in MiB.</param>
/// <param name="SendLimitPerHour">The new hourly send limit; at most what the account's plan allows.</param>
/// <param name="Enabled">
/// Whether the owner has the mailbox switched on. A mailbox the panel suspended with its account stays
/// suspended whatever this says; the switch is the owner's, the suspension is the panel's.
/// </param>
/// <param name="MailboxId">The mailbox, from the route. Established by the server; never bound from the body.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal. Established by the server.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal. Established by the server.</param>
public sealed record UpdateMailboxCommand(
    int QuotaMib,
    int SendLimitPerHour,
    bool Enabled,
    [property: JsonIgnore][property: BindNever][BindNever] Guid MailboxId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
