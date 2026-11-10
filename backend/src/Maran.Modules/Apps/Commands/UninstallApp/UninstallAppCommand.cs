using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Apps.Commands.UninstallApp;

/// <summary>
/// Removes one app from this server as a recorded task (issue #517). Administrator only. Refused while a
/// site uses the app, and the refusal lists those sites.
/// </summary>
/// <param name="AppId">
/// The catalogue id from the route. Server-established, guarded as
/// <see cref="InstallApp.InstallAppCommand.AppId"/> is.
/// </param>
/// <param name="IpAddress">The caller's address, for the audit journal; guarded the same way.</param>
/// <param name="UserAgent">The caller's user agent, for the audit journal; guarded the same way.</param>
public sealed record UninstallAppCommand(
    [property: JsonIgnore][property: BindNever][BindNever] string AppId = "",
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
