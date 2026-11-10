using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.ServerMigration.Commands.RemoveRemoteServer;

/// <summary>
/// Removes a registered remote server with everything stored about it: the private key, the pinned host key and
/// whatever its discovery found. The panel's public key stays installed on the server — only the administrator can
/// take it out — so the answer carries that key to tell them which one to remove.
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
public sealed record RemoveRemoteServerCommand(
    [property: JsonIgnore][property: BindNever][BindNever] Guid ServerId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
