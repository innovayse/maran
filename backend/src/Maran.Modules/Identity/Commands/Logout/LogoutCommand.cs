using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Identity.Commands.Logout;

/// <summary>Ends the session the caller's refresh token belongs to.</summary>
/// <remarks>
/// Identified by the refresh token rather than by the access token's <c>sid</c> claim, because
/// signing out has to work when the access token has already expired — which is exactly when a
/// user reaches for it after leaving a tab open overnight.
/// </remarks>
/// <param name="RefreshToken">The token read from the caller's cookie.</param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and
/// stamped by the action. Never bound from the request body (rules/csharp.md "Server-established
/// members of a command").</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request
/// headers and stamped by the action. Never bound from the request body.</param>
public sealed record LogoutCommand(
    string RefreshToken,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
