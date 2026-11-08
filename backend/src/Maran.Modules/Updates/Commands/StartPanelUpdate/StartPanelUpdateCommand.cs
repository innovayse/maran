using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Updates.Commands.StartPanelUpdate;

/// <summary>
/// Asks the root update service to update the panel (issue #382). It carries nothing the caller chooses:
/// no version, address, path or channel exists to send, and the release is whatever the signed manifest
/// names (rules/security.md items 1 and 12).
/// </summary>
/// <param name="IpAddress">
/// The caller's address, for the audit journal. Server-established: <c>JsonIgnore</c> closes the JSON
/// body and the bare <c>BindNever</c> closes the query string, the form and the route
/// (rules/csharp.md "Server-established members of a command").
/// </param>
/// <param name="UserAgent">The caller's user agent, for the audit journal; guarded as <paramref name="IpAddress"/> is.</param>
/// <param name="CorrelationId">The request's correlation id, which lines the task up with the log; guarded as <paramref name="IpAddress"/> is.</param>
public sealed record StartPanelUpdateCommand(
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "",
    [property: JsonIgnore][property: BindNever][BindNever] string CorrelationId = "");
