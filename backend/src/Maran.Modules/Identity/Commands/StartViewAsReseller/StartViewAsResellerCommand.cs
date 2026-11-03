using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Identity.Commands.StartViewAsReseller;

/// <summary>Starts viewing the panel as the login of one reseller (issues #317, #179).</summary>
/// <param name="ResellerId">The reseller to view as; the only member the caller supplies.</param>
/// <param name="OperatorUserId">The operator, established by the server from their own access token
/// and stamped by the action. Never bound — a bound id would let a caller name somebody else as the
/// operator in the journal.</param>
/// <param name="OperatorSessionId">The operator's own session, from their access token's <c>sid</c>,
/// stamped by the action. Never bound.</param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and
/// stamped by the action. Never bound (rules/csharp.md "Server-established members of a command").</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request
/// headers and stamped by the action. Never bound.</param>
public sealed record StartViewAsResellerCommand(
    Guid ResellerId,
    [property: JsonIgnore][property: BindNever][BindNever] Guid OperatorUserId = default,
    [property: JsonIgnore][property: BindNever][BindNever] Guid OperatorSessionId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
