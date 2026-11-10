using System.Text.Json.Serialization;
using Maran.Modules.ServerMigration.Interfaces;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.ServerMigration.Commands.AddRemoteServer;

/// <summary>
/// Registers a remote server the panel may read from. The panel makes the key it will log in with, reads the host
/// keys the server offers, and stores the server awaiting the administrator's confirmation of one of them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing the caller supplies is a credential.</b> There is no password and no key in the request: the
/// administrator installs the panel's public key on the server themselves, so the panel never holds a secret that was
/// typed into a form.
/// </para>
/// <para>
/// <b>Nothing is stored unless the server answers.</b> If the host keys cannot be read the registration is refused
/// and no key pair is kept.
/// </para>
/// </remarks>
/// <param name="Name">The label the administrator knows the server by; unique.</param>
/// <param name="Host">The server's DNS name or IP address literal.</param>
/// <param name="Port">The TCP port of the server's SSH daemon.</param>
/// <param name="SshUser">The login the panel's key is installed for on the server.</param>
/// <param name="AllowedFrom">
/// The addresses the key may be used from, as a comma-separated list of addresses or ranges, or <c>null</c> for no
/// restriction. It restricts the line the administrator installs; it does not change where the panel connects from.
/// </param>
/// <param name="IpAddress">
/// The caller's address, recorded in the audit journal. Established by the server and stamped by the action; never
/// bound from the request, which is the thing being audited.
/// </param>
/// <param name="UserAgent">
/// The caller's user agent, recorded in the audit journal. Established by the server and stamped by the action; never
/// bound from the request.
/// </param>
public sealed record AddRemoteServerCommand(
    string Name,
    string Host,
    int Port,
    string SshUser,
    string? AllowedFrom = null,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "") : IRemoteServerFields;
