using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.ServerMigration.Commands.TestRemoteServer;

/// <summary>
/// Tries the connection to a registered remote server with its pinned host key, runs the fixed read-only probes,
/// and records what they found, or why the server refused. Needs a confirmed host key.
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
public sealed record TestRemoteServerCommand(
    [property: JsonIgnore][property: BindNever][BindNever] Guid ServerId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
