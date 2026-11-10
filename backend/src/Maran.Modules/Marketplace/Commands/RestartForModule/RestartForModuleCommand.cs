using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Marketplace.Commands.RestartForModule;

/// <summary>
/// Asks the panel to restart so that a staged module takes effect. It carries nothing the caller chooses
/// but the module, and that comes from the route.
/// </summary>
/// <param name="Id">
/// The module id, from the route. Server-established: <c>JsonIgnore</c> closes the JSON body and the bare
/// <c>BindNever</c> closes the query string, the form and the route
/// (rules/csharp.md "Server-established members of a command").
/// </param>
public sealed record RestartForModuleCommand(
    [property: JsonIgnore][property: BindNever][BindNever] string Id = "");
