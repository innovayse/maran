namespace Maran.Host.Demo.Guard;

/// <summary>
/// The mutating endpoints that are not controller actions and that the demo allows. Today that is the
/// global search, a POST that reads and stores nothing.
/// </summary>
/// <remarks>
/// Any other mutating endpoint without a controller action is refused as unclassified, so a minimal
/// endpoint added later is closed until it is named here.
/// </remarks>
public static class DemoMinimalEndpointPolicy
{
    /// <summary>The allowed endpoints as <c>METHOD pattern</c>, the pattern spelled as it was mapped.</summary>
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST /api/v1/search",
    };

    /// <summary>Finds the rule for a minimal endpoint.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="pattern">The route pattern text the endpoint was mapped with.</param>
    /// <returns>An uncounted allowance, or <c>null</c> when the endpoint is not named.</returns>
    public static DemoEndpointRule? RuleFor(string method, string? pattern)
    {
        if (pattern is not null && Allowed.Contains($"{method} {pattern}"))
        {
            return DemoEndpointRule.AllowUncounted();
        }

        return null;
    }
}
