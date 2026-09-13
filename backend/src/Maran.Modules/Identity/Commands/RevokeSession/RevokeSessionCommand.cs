using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Identity.Commands.RevokeSession;

/// <summary>Ends one named session of the calling user.</summary>
/// <param name="SessionId">The session to act on, established by the server from the route and
/// stamped by the action. Never bound from the request body, so a body id can never disagree
/// with the URL.</param>
/// <param name="UserId">The caller, established by the server from their own access token and
/// stamped by the action. Never bound from the request body — a bound user id would let any
/// authenticated caller aim this command at somebody else's account.</param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and
/// stamped by the action. Never bound from the request body (rules/csharp.md "Server-established
/// members of a command").</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request
/// headers and stamped by the action. Never bound from the request body.</param>
public sealed record RevokeSessionCommand(
    [property: JsonIgnore][property: BindNever][BindNever] Guid SessionId,
    [property: JsonIgnore][property: BindNever][BindNever] Guid UserId,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
