using System.Text.Json.Serialization;
using Maran.Modules.Identity.Common;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Identity.Commands.SaveDashboardLayout;

/// <summary>
/// Replaces the calling person's whole dashboard layout with the two lists sent. Whatever the person
/// had stored and is not sent again is forgotten, so the SPA sends every entry it received, keys it
/// cannot draw included.
/// </summary>
/// <param name="Widgets">The widgets in the order they are drawn, each with whether it is hidden.</param>
/// <param name="Actions">The shortcuts of the quick-actions bar in the order they are drawn, each with whether it is hidden.</param>
/// <param name="UserId">The caller, established by the server from their own access token and
/// stamped by the action. Never bound from the request body — a bound user id would let any
/// authenticated caller replace somebody else's layout.</param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and
/// stamped by the action. Never bound from the request body (rules/csharp.md "Server-established
/// members of a command").</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request
/// headers and stamped by the action. Never bound from the request body.</param>
public sealed record SaveDashboardLayoutCommand(
    IReadOnlyList<DashboardLayoutEntryDto> Widgets,
    IReadOnlyList<DashboardLayoutEntryDto> Actions,
    [property: JsonIgnore][property: BindNever][BindNever] Guid UserId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
