using System.Text.Json.Serialization;
using Maran.Modules.ServerMigration.Interfaces;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.ServerMigration.Commands.EditRemoteServer;

/// <summary>
/// Changes the label of a registered remote server or where the panel connects to it. Changing the host, the port or
/// the login withdraws the confirmed host key, because the key was confirmed for the machine that answered there.
/// </summary>
/// <remarks>
/// The panel's key pair is not touched: the administrator's installed line stays valid, and only its
/// <c>from=</c> restriction changes with <c>AllowedFrom</c>.
/// </remarks>
/// <param name="Name">The new label; unique.</param>
/// <param name="Host">The server's DNS name or IP address literal.</param>
/// <param name="Port">The TCP port of the server's SSH daemon.</param>
/// <param name="SshUser">The login the panel's key is installed for on the server.</param>
/// <param name="AllowedFrom">The addresses the key may be used from, or <c>null</c> for no restriction.</param>
/// <param name="ServerId">
/// The server, from the route. Established by the action and never bound from the body, so a request cannot name one
/// server in the URL and another in the document.
/// </param>
/// <param name="IpAddress">
/// The caller's address, recorded in the audit journal. Established by the server and stamped by the action; never
/// bound from the request, which is the thing being audited.
/// </param>
/// <param name="UserAgent">
/// The caller's user agent, recorded in the audit journal. Established by the server and stamped by the action; never
/// bound from the request.
/// </param>
public sealed record EditRemoteServerCommand(
    string Name,
    string Host,
    int Port,
    string SshUser,
    string? AllowedFrom = null,
    [property: JsonIgnore][property: BindNever][BindNever] Guid ServerId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "") : IRemoteServerFields;
