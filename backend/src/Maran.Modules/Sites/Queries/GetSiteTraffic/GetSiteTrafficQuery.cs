using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Sites.Queries.GetSiteTraffic;

/// <summary>Reads what one site's access log counted over a window of whole UTC days (issue #152).</summary>
/// <param name="Days">
/// The window's length, one of the lengths the choices endpoint lists; none for the first of them, so the
/// default is the server's and not the SPA's.
/// </param>
/// <param name="SiteId">
/// The site to read, established by the server from the route and stamped by the action. Never bound from
/// the query string. Another tenant's id answers "not found".
/// </param>
public sealed record GetSiteTrafficQuery(
    int? Days = null,
    [property: JsonIgnore][property: BindNever][BindNever] Guid SiteId = default);
