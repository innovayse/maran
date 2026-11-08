using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Mail.Commands.DeleteMailDomain;

/// <summary>
/// Deletes a mail domain together with everything that lives under it: its mailboxes (and their mail),
/// aliases, autoresponders and DKIM keys.
/// </summary>
/// <param name="MailDomainId">The domain to delete; comes from the route.</param>
/// <param name="IpAddress">
/// The caller's address, recorded in the audit journal. Established by the server and stamped by the
/// action; never bound from the request.
/// </param>
/// <param name="UserAgent">
/// The caller's user agent, recorded in the audit journal. Established by the server and stamped by
/// the action; never bound from the request.
/// </param>
public sealed record DeleteMailDomainCommand(
    [property: JsonIgnore][property: BindNever][BindNever] Guid MailDomainId,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
