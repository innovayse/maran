using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Sites.Queries.ListPhpExtensions;

/// <summary>
/// Lists the catalogue PHP extensions of one installed PHP version and whether each is loaded — the
/// reference data the extensions panel renders, so a name is never typed
/// (rules/architecture.md "The backend owns the data, the SPA renders it").
/// </summary>
/// <param name="Version">
/// The installed PHP version, as the packages name it ("8.3"). It comes from the route and is stamped
/// by the action.
/// </param>
public sealed record ListPhpExtensionsQuery(
    [property: JsonIgnore][property: BindNever][BindNever] string Version = "");
