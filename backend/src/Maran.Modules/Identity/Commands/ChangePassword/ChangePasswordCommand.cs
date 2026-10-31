using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Identity.Commands.ChangePassword;

/// <summary>Replaces the signed-in caller's password, proving the current one first.</summary>
/// <param name="CurrentPassword">The password the caller signs in with today. Never logged, never
/// stored, never audited.</param>
/// <param name="NewPassword">The replacement. Never logged, never audited; only its Argon2id hash is
/// stored.</param>
/// <param name="UserId">The caller, established by the server from their own access token and
/// stamped by the action. Never bound from the request — a bound user id would let any authenticated
/// caller aim this command at somebody else's account.</param>
/// <param name="CurrentSessionId">The session the caller's access token was issued for (its
/// <c>sid</c> claim), stamped by the action. Its rotation family is the one sign-in this command
/// keeps; every other session of the user is revoked. Never bound from the request.</param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and
/// stamped by the action. Never bound from the request body (rules/csharp.md "Server-established
/// members of a command").</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request
/// headers and stamped by the action. Never bound from the request body.</param>
public sealed record ChangePasswordCommand(
    string CurrentPassword,
    string NewPassword,
    [property: JsonIgnore][property: BindNever][BindNever] Guid UserId = default,
    [property: JsonIgnore][property: BindNever][BindNever] Guid CurrentSessionId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
