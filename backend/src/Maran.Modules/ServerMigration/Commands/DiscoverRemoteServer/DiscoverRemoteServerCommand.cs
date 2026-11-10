using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.ServerMigration.Commands.DiscoverRemoteServer;

/// <summary>
/// Reads the inventory of a registered remote server — its accounts, sites, databases and scheduled jobs — and
/// replaces what the panel knew of it. Read-only on the server. Needs a confirmed host key and a server that is not
/// in a failed state.
/// </summary>
/// <param name="ServerId">
/// The server, from the route. Established by the action and never bound from the body.
/// </param>
/// <param name="IpAddress">
/// The caller's address, recorded in the audit journal. Established by the server and stamped by the action; never
/// bound from the request, which is the thing being audited.
/// </param>
/// <param name="UserAgent">
/// The caller's user agent, recorded in the audit journal. Established by the server and stamped by the action; never
/// bound from the request.
/// </param>
public sealed record DiscoverRemoteServerCommand(
    [property: JsonIgnore][property: BindNever][BindNever] Guid ServerId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
