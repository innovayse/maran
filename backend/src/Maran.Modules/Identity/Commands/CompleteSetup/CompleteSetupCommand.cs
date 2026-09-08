using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Identity.Commands.CompleteSetup;

/// <summary>Creates the panel's first administrator, using the installer's one-time token.</summary>
/// <param name="Token">The token from the installer's one-time link.</param>
/// <param name="Username">The administrator's login name.</param>
/// <param name="Email">The administrator's contact address.</param>
/// <param name="Password">The plaintext password. Never logged, never audited.</param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and
/// stamped by the action. Never bound from the request body (rules/csharp.md "Server-established
/// members of a command").</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request
/// headers and stamped by the action. Never bound from the request body.</param>
public sealed record CompleteSetupCommand(
    string Token,
    string Username,
    string Email,
    string Password,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
