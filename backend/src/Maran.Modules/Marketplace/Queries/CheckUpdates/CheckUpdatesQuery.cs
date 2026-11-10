using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Marketplace.Queries.CheckUpdates;

/// <summary>
/// Asks the marketplace which installed modules have a newer version or were withdrawn. It is a query
/// because it reads, and it is only ever sent when the administrator presses check or opens the updates tab.
/// </summary>
/// <param name="IpAddress">
/// The caller's address, for the audit journal. Server-established: <c>JsonIgnore</c> closes the JSON
/// body and the bare <c>BindNever</c> closes the query string, the form and the route
/// (rules/csharp.md "Server-established members of a command").
/// </param>
/// <param name="UserAgent">The caller's user agent, for the audit journal; guarded as <paramref name="IpAddress"/> is.</param>
public sealed record CheckUpdatesQuery(
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
