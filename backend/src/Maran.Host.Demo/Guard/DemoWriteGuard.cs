using Maran.Host.Demo.Guard.Enums;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace Maran.Host.Demo.Guard;

/// <summary>
/// Decides, for one request, whether the public demo lets it run: every mutating request must be named
/// by <see cref="DemoEndpointTable"/> (or by <see cref="DemoMinimalEndpointPolicy"/>) and allowed, and the
/// ones that name a protected resource must not name one.
/// </summary>
/// <remarks>
/// <b>Fail closed.</b> A mutating request that reaches an endpoint nobody classified is refused as
/// <see cref="DemoRefusalArea.Unclassified"/>, and so is every action that depends on the protected set
/// (<see cref="DemoAccess.AllowedUnlessProtected"/>) while that set has not been loaded
/// (<see cref="DemoProtectedResources.IsLoaded"/>): an empty set would otherwise allow all of them. A request with
/// no endpoint matched (a 404) and a read (GET, HEAD, OPTIONS, TRACE) are not judged: a read changes nothing, and a
/// missing route runs nothing.
/// </remarks>
public sealed class DemoWriteGuard
{
    /// <summary>The methods that change nothing.</summary>
    private static readonly string[] SafeMethods = ["GET", "HEAD", "OPTIONS", "TRACE"];

    /// <summary>The table of controller actions.</summary>
    private readonly DemoEndpointPolicy _policy;

    /// <summary>The resources the seed marked as protected.</summary>
    private readonly DemoProtectedResources _protected;

    /// <summary>Creates the guard.</summary>
    /// <param name="policy">The table of controller actions.</param>
    /// <param name="protectedResources">The resources the seed marked as protected.</param>
    public DemoWriteGuard(DemoEndpointPolicy policy, DemoProtectedResources protectedResources)
    {
        _policy = policy;
        _protected = protectedResources;
    }

    /// <summary>Whether an HTTP method changes things.</summary>
    /// <param name="method">The method.</param>
    /// <returns><c>true</c> for everything but a safe method.</returns>
    public static bool IsMutation(string method)
    {
        return !SafeMethods.Contains(method, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A short name for an endpoint, for the journal: its action id, or its method and pattern.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="endpoint">The matched endpoint, or <c>null</c>.</param>
    /// <returns>The label; never carries a route value, so it holds no id or name a visitor typed.</returns>
    public static string LabelOf(string method, Endpoint? endpoint)
    {
        var descriptor = endpoint?.Metadata.GetMetadata<ControllerActionDescriptor>();
        if (descriptor is not null)
        {
            return DemoEndpointPolicy.ActionIdOf(descriptor.MethodInfo);
        }

        return $"{method} {(endpoint as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched"}";
    }

    /// <summary>Judges one request.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="endpoint">The matched endpoint, or <c>null</c> when none matched.</param>
    /// <param name="routeValues">The route values of the request.</param>
    /// <returns>The decision.</returns>
    public DemoGuardDecision Decide(string method, Endpoint? endpoint, RouteValueDictionary routeValues)
    {
        if (endpoint is null || !IsMutation(method))
        {
            return DemoGuardDecision.PassUncounted;
        }

        var descriptor = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>();
        var rule = descriptor is not null
            ? _policy.RuleFor(DemoEndpointPolicy.ActionIdOf(descriptor.MethodInfo))
            : DemoMinimalEndpointPolicy.RuleFor(method, (endpoint as RouteEndpoint)?.RoutePattern.RawText);

        if (rule is null)
        {
            return DemoGuardDecision.Refuse(DemoRefusalArea.Unclassified);
        }

        return rule.Access switch
        {
            DemoAccess.Refused => DemoGuardDecision.Refuse(rule.Area!.Value),
            DemoAccess.AllowedUnlessProtected => !_protected.IsLoaded || NamesProtectedResource(rule, routeValues)
                ? DemoGuardDecision.Refuse(DemoRefusalArea.ProtectedResource)
                : DemoGuardDecision.PassCounted,
            _ => Passing(rule),
        };
    }

    /// <summary>The permitted decision a rule that is not a refusal and checks no resource gives.</summary>
    /// <param name="rule">The rule.</param>
    /// <returns>Counted against the mutation budgets, against the sign-in allowances, or not at all.</returns>
    private static DemoGuardDecision Passing(DemoEndpointRule rule)
    {
        if (rule.CountsTowardSignInBudget)
        {
            return DemoGuardDecision.PassSignIn;
        }

        return rule.CountsTowardBudget ? DemoGuardDecision.PassCounted : DemoGuardDecision.PassUncounted;
    }

    /// <summary>Whether the route value a rule points at names a protected resource.</summary>
    /// <param name="rule">A rule with a protected route key.</param>
    /// <param name="routeValues">The route values of the request.</param>
    /// <returns><c>true</c> when it parses as an id in the protected set; an unparseable value is not judged.</returns>
    private bool NamesProtectedResource(DemoEndpointRule rule, RouteValueDictionary routeValues)
    {
        return routeValues.TryGetValue(rule.ProtectedRouteKey!, out var raw)
            && Guid.TryParse(Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture), out var id)
            && _protected.IsProtected(id);
    }
}
