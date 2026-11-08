using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Mail.Commands.DeleteMailAlias;

/// <summary>
/// Removes one alias, forwarder or catch-all. Mail sent to the address is no longer accepted for it;
/// nothing else of the domain changes.
/// </summary>
/// <param name="MailAliasId">
/// Which address to remove, from the route. A row identifier and never an address: the row says who owns
/// it, and another account's identifier answers "not found". Established by the server.
/// </param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal. Established by the server.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal. Established by the server.</param>
public sealed record DeleteMailAliasCommand(
    [property: JsonIgnore][property: BindNever][BindNever] Guid MailAliasId,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
