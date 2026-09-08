using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Identity.Commands.Login;

/// <summary>Signs a user in with a username and password.</summary>
/// <param name="Username">The login name supplied by the caller.</param>
/// <param name="Password">The plaintext password. Never logged, never stored, never audited.</param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and
/// stamped by the action. Never bound from the request body (rules/csharp.md "Server-established
/// members of a command").</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request
/// headers and stamped by the action. Never bound from the request body.</param>
public sealed record LoginCommand(
    string Username,
    string Password,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
