using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Marketplace.Commands.RollbackModule;

/// <summary>
/// Asks the panel to go back to the version an installed module had before its last change. The administrator proves their password again first.
/// </summary>
/// <param name="Password">The administrator's current password.</param>
/// <param name="Id">
/// The module id, from the route. Server-established: <c>JsonIgnore</c> closes the JSON body and the bare
/// <c>BindNever</c> closes the query string, the form and the route, so the id in the URL is the only one
/// there is (rules/csharp.md "Server-established members of a command").
/// </param>
/// <param name="UserId">The administrator, from their own access token; guarded as <paramref name="Id"/> is.</param>
/// <param name="IpAddress">The caller's address, for the audit journal; guarded as <paramref name="Id"/> is.</param>
/// <param name="UserAgent">The caller's user agent, for the audit journal; guarded as <paramref name="Id"/> is.</param>
public sealed record RollbackModuleCommand(
    string Password,
    [property: JsonIgnore][property: BindNever][BindNever] string Id = "",
    [property: JsonIgnore][property: BindNever][BindNever] Guid UserId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
