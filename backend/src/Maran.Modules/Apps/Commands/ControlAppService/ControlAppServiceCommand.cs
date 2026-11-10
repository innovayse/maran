using System.Text.Json.Serialization;
using Maran.Modules.Apps.Domain.Enums;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Apps.Commands.ControlAppService;

/// <summary>
/// Starts, stops, restarts or reloads the service of one installed app (issue #517). Administrator only.
/// The effect is host-wide: stopping a web server takes every site it serves off the air.
/// </summary>
/// <param name="AppId">
/// The catalogue id from the route. Server-established, guarded as
/// <see cref="InstallApp.InstallAppCommand.AppId"/> is.
/// </param>
/// <param name="Action">The action from the route (<c>start</c>, <c>stop</c>, <c>restart</c>, <c>reload</c>); guarded the same way.</param>
/// <param name="IpAddress">The caller's address, for the audit journal; guarded the same way.</param>
/// <param name="UserAgent">The caller's user agent, for the audit journal; guarded the same way.</param>
public sealed record ControlAppServiceCommand(
    [property: JsonIgnore][property: BindNever][BindNever] string AppId = "",
    [property: JsonIgnore][property: BindNever][BindNever] AppServiceAction Action = AppServiceAction.Start,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
