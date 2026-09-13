using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// Reads the routes a controller actually declares, so an IDOR fixture can assert its own
/// completeness instead of trusting a hand-written list.
/// </summary>
/// <remarks>
/// One implementation for every controller, because the two that existed carried the same defect
/// twice. Each normalised the single literal <c>{id:guid}</c> and each decided "this route names one
/// resource" by looking for the literal string <c>{id}</c> — so a route spelled <c>{siteId:guid}</c>
/// would be reported missing from the full list (correct), and the moment somebody silenced that by
/// adding the row, the tenant-scoped half would go quiet and the route would have no
/// 404-never-403 coverage while the fixture's own documentation claimed otherwise. That is the exact
/// history those fixtures recount about the PHP-version rebind, one parameter name later.
///
/// Both questions are answered here by the SHAPE of the route rather than by one spelling: any
/// <c>{name:constraint}</c> normalises to <c>{name}</c>, and a route is resource-scoped when it has
/// a parameter at all.
/// </remarks>
public static class ControllerRoutes
{
    /// <summary>Matches one route parameter, with or without constraints: <c>{id:guid}</c>.</summary>
    private static readonly Regex Parameter = new(@"\{([^:}]+)(:[^}]*)?\}", RegexOptions.Compiled);

    /// <summary>Reads every route a controller declares, as "METHOD /path".</summary>
    /// <typeparam name="TController">The controller to read.</typeparam>
    /// <returns>Route strings, constraints stripped, ordered and deduplicated.</returns>
    public static List<string> Declared<TController>()
    {
        // inherit: false — BaseApiController carries its own [Route] convention, and the derived
        // controller's own template is the one that binds (rules/csharp.md "Controller shape is fixed").
        var prefix = typeof(TController)
            .GetCustomAttributes(typeof(RouteAttribute), inherit: false)
            .Cast<RouteAttribute>()
            .Single()
            .Template;
        const BindingFlags DeclaredOnly = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        return typeof(TController)
            .GetMethods(DeclaredOnly)
            .SelectMany(method =>
            {
                return method.GetCustomAttributes<HttpMethodAttribute>();
            })
            .Select(attribute =>
            {
                var verb = attribute.HttpMethods.First();
                var suffix = string.IsNullOrEmpty(attribute.Template) ? string.Empty : "/" + attribute.Template;
                return Normalize($"{verb} /{prefix}{suffix}");
            })
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Whether a route names one resource — that is, whether it has a route parameter.</summary>
    /// <param name="route">A route string from <see cref="Declared{TController}"/> or a fixture.</param>
    /// <returns><c>true</c> when the route carries at least one parameter.</returns>
    public static bool IsResourceScoped(string route)
    {
        return Parameter.IsMatch(route);
    }

    /// <summary>Strips route constraints, so <c>{id:guid}</c> and <c>{id}</c> are the same route.</summary>
    /// <param name="route">The route to normalise.</param>
    /// <returns>The route with every parameter reduced to its name.</returns>
    private static string Normalize(string route)
    {
        return Parameter.Replace(route, "{$1}");
    }
}
