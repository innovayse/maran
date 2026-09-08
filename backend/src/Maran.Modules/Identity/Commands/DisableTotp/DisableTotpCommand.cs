using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Identity.Commands.DisableTotp;

/// <summary>Turns the second factor off.</summary>
/// <param name="UserId">The caller, established by the server from their own access token and
/// stamped by the action. Never bound from the request body — a bound user id would let any
/// authenticated caller aim this command at somebody else's account.</param>
/// <param name="Code">A current code or a recovery code, proving the factor is still in their hands.</param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and
/// stamped by the action. Never bound from the request body (rules/csharp.md "Server-established
/// members of a command").</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request
/// headers and stamped by the action. Never bound from the request body.</param>
public sealed record DisableTotpCommand(
    [property: JsonIgnore][property: BindNever][BindNever] Guid UserId,
    string Code,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
