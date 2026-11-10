using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Marketplace.Queries.ListCatalogue;

/// <summary>Lists one page of the marketplace catalogue.</summary>
/// <param name="Channel">The release channel, or null for the configured one.</param>
/// <param name="Search">A search term, or null for the whole catalogue.</param>
/// <param name="Cursor">The previous page's cursor, or null for the first page.</param>
/// <param name="IpAddress">
/// The caller's address, for the audit journal. Server-established: <c>JsonIgnore</c> closes the JSON
/// body and the bare <c>BindNever</c> closes the query string, the form and the route
/// (rules/csharp.md "Server-established members of a command").
/// </param>
/// <param name="UserAgent">The caller's user agent, for the audit journal; guarded as <paramref name="IpAddress"/> is.</param>
public sealed record ListCatalogueQuery(
    string? Channel = null,
    string? Search = null,
    string? Cursor = null,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
