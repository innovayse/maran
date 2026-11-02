using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Identity.Commands.EndViewAs;

/// <summary>Ends the view-as session the caller's token belongs to: the banner's Back (issue #317).</summary>
/// <param name="SessionId">The caller's session, from their access token's <c>sid</c>, stamped by the
/// action. Never bound — the only session this can end is the one the caller is holding.</param>
/// <param name="ViewedUserId">The login the caller's token speaks for, stamped by the action. Never bound.</param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and
/// stamped by the action. Never bound (rules/csharp.md "Server-established members of a command").</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request
/// headers and stamped by the action. Never bound.</param>
public sealed record EndViewAsCommand(
    [property: JsonIgnore][property: BindNever][BindNever] Guid SessionId,
    [property: JsonIgnore][property: BindNever][BindNever] Guid ViewedUserId,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
