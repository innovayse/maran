using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Marketplace.Queries.GetModuleDetails;

/// <summary>Reads one module's details with every version the marketplace offers for this panel.</summary>
/// <param name="Id">
/// The module id, from the route. Server-established: <c>JsonIgnore</c> closes the JSON body and the bare
/// <c>BindNever</c> closes the query string, the form and the route
/// (rules/csharp.md "Server-established members of a command").
/// </param>
/// <param name="IpAddress">The caller's address, for the audit journal; guarded as <paramref name="Id"/> is.</param>
/// <param name="UserAgent">The caller's user agent, for the audit journal; guarded as <paramref name="Id"/> is.</param>
public sealed record GetModuleDetailsQuery(
    [property: JsonIgnore][property: BindNever][BindNever] string Id = "",
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
