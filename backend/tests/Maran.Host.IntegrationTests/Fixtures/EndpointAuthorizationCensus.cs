using System.Text.RegularExpressions;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// Reads the authorization metadata of composed endpoints and names every one that does not state
/// its audience: an endpoint must either be marked anonymous AND be pinned, or name one of the
/// panel's two policies.
/// </summary>
/// <remarks>
/// <para>
/// Pure functions over endpoints the caller supplies, so the same rule runs over the real route
/// table and over hand-built endpoints that prove each branch can fire. A check that has never been
/// seen to fail is not a check.
/// </para>
/// <para>
/// What counts as naming a policy is strict on purpose. A bare <c>[Authorize]</c> evaluates the
/// default policy, a role-only one evaluates whatever the roles say, and an unknown policy name
/// throws at request time; each is a way to be customer-level or broken without saying so. An
/// <see cref="AuthorizationPolicy"/> object in the metadata is refused as well, because it carries
/// no name to review.
/// </para>
/// </remarks>
public static class EndpointAuthorizationCensus
{
    /// <summary>Matches one route parameter, with or without constraints: <c>{id:guid}</c>.</summary>
    private static readonly Regex Parameter = new(@"\{([^:}]+)(:[^}]*)?\}", RegexOptions.Compiled);

    /// <summary>The only policy names an endpoint may carry.</summary>
    private static readonly string[] KnownPolicies =
    {
        AuthorizationPolicies.AdminOnly,
        AuthorizationPolicies.AnyAuthenticated,
    };

    /// <summary>Spells an endpoint as <c>VERB /path</c>, constraints stripped.</summary>
    /// <param name="endpoint">The endpoint to name.</param>
    /// <returns>The key the anonymous pin uses, e.g. <c>POST /api/v1/auth/login</c>.</returns>
    public static string RouteKey(RouteEndpoint endpoint)
    {
        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
        var verb = methods is { Count: > 0 } ? string.Join(',', methods) : "ANY";
        var path = "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/');

        return $"{verb} {Parameter.Replace(path, "{$1}")}";
    }

    /// <summary>Finds every endpoint that does not state its audience.</summary>
    /// <param name="endpoints">The endpoints to judge.</param>
    /// <param name="pin">The routes that may be anonymous.</param>
    /// <returns>One message per violation, each naming the route; empty when none.</returns>
    public static List<string> FindViolations(
        IEnumerable<RouteEndpoint> endpoints,
        IReadOnlyCollection<PinnedAnonymousEndpoint> pin)
    {
        var pinned = pin.Select(entry =>
        {
            return entry.Route;
        }).ToHashSet(StringComparer.Ordinal);
        var violations = new List<string>();

        foreach (var endpoint in endpoints)
        {
            var key = RouteKey(endpoint);
            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            {
                if (!pinned.Contains(key))
                {
                    violations.Add($"{key}: is anonymous but is not in the anonymous pin.");
                }

                continue;
            }

            violations.AddRange(ProblemsOf(endpoint).Select(problem =>
            {
                return $"{key}: {problem}";
            }));
        }

        return violations;
    }

    /// <summary>Finds every pinned route that is not an anonymous endpoint of the composed panel.</summary>
    /// <param name="endpoints">The endpoints the panel composed.</param>
    /// <param name="pin">The pin to check.</param>
    /// <returns>One message per stale or incomplete entry; empty when the pin is exact.</returns>
    public static List<string> FindStalePins(
        IEnumerable<RouteEndpoint> endpoints,
        IReadOnlyCollection<PinnedAnonymousEndpoint> pin)
    {
        var anonymous = endpoints
            .Where(endpoint =>
            {
                return endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            })
            .Select(RouteKey)
            .ToHashSet(StringComparer.Ordinal);
        var stale = new List<string>();

        foreach (var entry in pin)
        {
            if (!anonymous.Contains(entry.Route))
            {
                stale.Add($"{entry.Route}: is pinned but is not an anonymous endpoint.");
            }

            if (string.IsNullOrWhiteSpace(entry.Reason))
            {
                stale.Add($"{entry.Route}: is pinned without a reason.");
            }
        }

        return stale;
    }

    /// <summary>Lists what is wrong with a non-anonymous endpoint's authorization metadata.</summary>
    /// <param name="endpoint">The endpoint to judge.</param>
    /// <returns>The problems, empty when the endpoint names a known policy and nothing else.</returns>
    private static List<string> ProblemsOf(RouteEndpoint endpoint)
    {
        var problems = new List<string>();
        var data = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();

        if (data.Count == 0)
        {
            problems.Add("declares no authorization (it would fall to the fallback policy).");
        }

        if (endpoint.Metadata.GetMetadata<AuthorizationPolicy>() is not null)
        {
            problems.Add("carries an authorization policy object instead of a named policy.");
        }

        foreach (var item in data)
        {
            if (string.IsNullOrEmpty(item.Policy))
            {
                problems.Add("carries an authorize declaration that names no policy (bare or roles only).");
            }
            else if (!KnownPolicies.Contains(item.Policy, StringComparer.Ordinal))
            {
                problems.Add($"names the unknown policy '{item.Policy}'.");
            }

            if (!string.IsNullOrEmpty(item.Roles))
            {
                problems.Add("restricts by roles, which the policies already decide.");
            }

            if (!string.IsNullOrEmpty(item.AuthenticationSchemes))
            {
                problems.Add("restricts by authentication scheme.");
            }
        }

        return problems;
    }
}
