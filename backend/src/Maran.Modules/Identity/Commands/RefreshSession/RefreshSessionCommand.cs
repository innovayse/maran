using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Identity.Commands.RefreshSession;

/// <summary>Exchanges a refresh token for a new access token and a new refresh token.</summary>
/// <param name="RefreshToken">The token read from the caller's cookie.</param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and
/// stamped by the action. Never bound from the request body (rules/csharp.md "Server-established
/// members of a command").</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request
/// headers and stamped by the action. Never bound from the request body.</param>
public sealed record RefreshSessionCommand(
    string RefreshToken,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
