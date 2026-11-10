using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.ServerMigration.Commands.RescanRemoteServerHostKeys;

/// <summary>
/// Asks a registered remote server for the host keys it offers now and replaces the candidates the administrator is
/// shown. It never pins a key: a rescan is how an administrator learns that the key changed, and trusting whatever
/// answered would turn it into the attack it is meant to reveal.
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
public sealed record RescanRemoteServerHostKeysCommand(
    [property: JsonIgnore][property: BindNever][BindNever] Guid ServerId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
