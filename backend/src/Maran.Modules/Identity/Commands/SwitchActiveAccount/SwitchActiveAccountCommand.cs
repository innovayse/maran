using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Identity.Commands.SwitchActiveAccount;

/// <summary>Makes the caller's current sign-in act for another hosting account they hold a membership of.</summary>
/// <param name="AccountId">The account to act for, from the request body. Whether the caller holds a
/// membership of it is decided by the handler, never assumed from the value.</param>
/// <param name="UserId">The caller, established by the server from their own access token and
/// stamped by the action. Never bound from the request body — a bound user id would let any
/// authenticated caller switch somebody else's sign-in.</param>
/// <param name="SessionId">The caller's own session, established by the server from the access
/// token's <c>sid</c> claim and stamped by the action. Never bound from the request body.</param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and
/// stamped by the action. Never bound from the request body (rules/csharp.md "Server-established
/// members of a command").</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request
/// headers and stamped by the action. Never bound from the request body.</param>
public sealed record SwitchActiveAccountCommand(
    Guid AccountId,
    [property: JsonIgnore][property: BindNever][BindNever] Guid UserId = default,
    [property: JsonIgnore][property: BindNever][BindNever] Guid SessionId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
