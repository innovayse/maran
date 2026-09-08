using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Identity.Commands.ResetPassword;

/// <summary>Sets a new password using a reset token, without knowing the old one.</summary>
/// <param name="Token">The plaintext token from the reset mail. Never logged, never audited.</param>
/// <param name="NewPassword">The new plaintext password. Never logged, never stored, never audited.</param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and
/// stamped by the action. Never bound from the request body (rules/csharp.md "Server-established
/// members of a command").</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request
/// headers and stamped by the action. Never bound from the request body.</param>
public sealed record ResetPasswordCommand(
    string Token,
    string NewPassword,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
