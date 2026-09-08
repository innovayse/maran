using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Identity.Commands.ConfirmTotpEnrolment;

/// <summary>Completes a two-factor enrolment by proving the secret works.</summary>
/// <param name="UserId">The caller, established by the server from their own access token and
/// stamped by the action. Never bound from the request body — a bound user id would let any
/// authenticated caller aim this command at somebody else's account.</param>
/// <param name="Secret">The secret handed out by the enrolment step.</param>
/// <param name="Code">A code the user's app produced from it.</param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and
/// stamped by the action. Never bound from the request body (rules/csharp.md "Server-established
/// members of a command").</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request
/// headers and stamped by the action. Never bound from the request body.</param>
public sealed record ConfirmTotpEnrolmentCommand(
    [property: JsonIgnore][property: BindNever][BindNever] Guid UserId,
    string Secret,
    string Code,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
