using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Identity.Commands.SaveAuditRetention;

/// <summary>Replaces how long the audit journal is kept.</summary>
/// <param name="RetentionDays">
/// Days a journal row is kept, from the panel's accepted minimum to its maximum; <c>null</c> keeps the
/// journal indefinitely. There is no value that means "delete everything now".
/// </param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and
/// stamped by the action. Never bound from the request body (rules/csharp.md "Server-established
/// members of a command").</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request
/// headers and stamped by the action. Never bound from the request body.</param>
public sealed record SaveAuditRetentionCommand(
    int? RetentionDays,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
