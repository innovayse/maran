using System.Text.Json.Serialization;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Monitoring.Commands.ChangeLogLevel;

/// <summary>Changes the level the panel's logger writes at, now and after every restart (issue #131).</summary>
/// <param name="Level">The level the administrator chose.</param>
/// <param name="IpAddress">
/// The caller's address, for the audit journal. Server-established: <c>JsonIgnore</c> closes the JSON
/// body and the bare <c>BindNever</c> closes the query string, the form and the route
/// (rules/csharp.md "Server-established members of a command").
/// </param>
/// <param name="UserAgent">The caller's user agent, for the audit journal; guarded as <paramref name="IpAddress"/> is.</param>
public sealed record ChangeLogLevelCommand(
    PanelLogLevel Level,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
