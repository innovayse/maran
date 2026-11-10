using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Apps.Commands.InstallApp;

/// <summary>
/// Installs one app of the catalogue on this server as a recorded task: a site web server, a PHP version
/// or a Node.js version (issue #517). Administrator only; it changes what software the server carries.
/// </summary>
/// <param name="AppId">
/// The catalogue id from the route (<c>nginx</c>, <c>apache</c>, <c>php-8.3</c>, <c>nodejs-22</c>).
/// Server-established: <c>JsonIgnore</c> closes the JSON body and the bare <c>BindNever</c> closes the
/// query string, the form and the route, so the id the action stamps from the URL is the only one there is
/// (rules/csharp.md "Server-established members of a command").
/// </param>
/// <param name="IpAddress">The caller's address, for the audit journal; guarded as <paramref name="AppId"/> is.</param>
/// <param name="UserAgent">The caller's user agent, for the audit journal; guarded as <paramref name="AppId"/> is.</param>
public sealed record InstallAppCommand(
    [property: JsonIgnore][property: BindNever][BindNever] string AppId = "",
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
