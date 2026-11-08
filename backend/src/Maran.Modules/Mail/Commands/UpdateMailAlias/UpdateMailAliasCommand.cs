using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Mail.Commands.UpdateMailAlias;

/// <summary>
/// Replaces where an existing alias, forwarder or catch-all delivers.
/// </summary>
/// <remarks>
/// Only the targets change: the address is its identity, so moving it is a delete and a create. The kind
/// follows the new targets (an alias that gains an outside target becomes a forwarder), and the catch-all
/// stays a catch-all.
/// </remarks>
/// <param name="Targets">Where the mail goes from now on: at least one and at most the module's ceiling, no repeats.</param>
/// <param name="MailAliasId">The address, from the route. Established by the server; never bound from the body.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal. Established by the server.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal. Established by the server.</param>
public sealed record UpdateMailAliasCommand(
    IReadOnlyList<string> Targets,
    [property: JsonIgnore][property: BindNever][BindNever] Guid MailAliasId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
