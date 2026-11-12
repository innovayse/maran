using Maran.Host.Demo.Guard.Enums;

namespace Maran.Host.Demo.Guard;

/// <summary>What the demo does with one mutating endpoint, built only through its named factories.</summary>
public sealed class DemoEndpointRule
{
    /// <summary>Whether the call runs, runs unless protected, or is refused.</summary>
    public DemoAccess Access { get; }

    /// <summary>
    /// The area that explains a refusal; set exactly when <see cref="Access"/> is <see cref="DemoAccess.Refused"/>.
    /// </summary>
    public DemoRefusalArea? Area { get; }

    /// <summary>
    /// The route value naming the resource to check against the protected set; set exactly when
    /// <see cref="Access"/> is <see cref="DemoAccess.AllowedUnlessProtected"/>.
    /// </summary>
    public string? ProtectedRouteKey { get; }

    /// <summary>
    /// Whether a call that runs spends the mutation budgets. False for sign-in, refresh and sign-out.
    /// </summary>
    public bool CountsTowardBudget { get; }

    /// <summary>
    /// Whether a call that runs draws on the sign-in allowances instead: sign-in, refresh and sign-out, which
    /// write session and audit rows but must never spend the mutation budgets. Never set together with
    /// <see cref="CountsTowardBudget"/>.
    /// </summary>
    public bool CountsTowardSignInBudget { get; }

    /// <summary>
    /// Whether the text a visitor typed in a call that runs is screened (<see cref="DemoContentFilter"/>) before
    /// the action stores it. Stated by each factory: every call that runs screens, except a sign-in, refresh or
    /// sign-out, whose text is a login name nobody else is shown. A refused call never runs, so screening it
    /// would judge text nothing stores.
    /// </summary>
    public bool ScreensText { get; }

    /// <summary>Creates a rule; use the factories, which cannot build an inconsistent one.</summary>
    /// <param name="access">The access.</param>
    /// <param name="area">The refusal area, required for a refusal and forbidden otherwise.</param>
    /// <param name="protectedRouteKey">The route value to check; required for a protected check, else null.</param>
    /// <param name="countsTowardBudget">Whether the call spends the mutation budgets.</param>
    /// <param name="screensText">Whether the text a visitor typed in the call is screened before it is stored.</param>
    /// <param name="countsTowardSignInBudget">Whether the call draws on the sign-in allowances instead.</param>
    private DemoEndpointRule(
        DemoAccess access,
        DemoRefusalArea? area,
        string? protectedRouteKey,
        bool countsTowardBudget,
        bool screensText,
        bool countsTowardSignInBudget = false)
    {
        Access = access;
        Area = area;
        ProtectedRouteKey = protectedRouteKey;
        CountsTowardBudget = countsTowardBudget;
        ScreensText = screensText;
        CountsTowardSignInBudget = countsTowardSignInBudget;
    }

    /// <summary>The call runs and spends the budgets.</summary>
    /// <returns>The rule.</returns>
    public static DemoEndpointRule Allow()
    {
        return new DemoEndpointRule(DemoAccess.Allowed, null, null, true, true);
    }

    /// <summary>The call runs and spends nothing: a call that writes no row a visitor can multiply.</summary>
    /// <returns>The rule.</returns>
    public static DemoEndpointRule AllowUncounted()
    {
        return new DemoEndpointRule(DemoAccess.Allowed, null, null, false, true);
    }

    /// <summary>
    /// The call runs and draws on the sign-in allowances, not the mutation budgets: it is how a visitor gets
    /// in, stays in and gets out, and it writes a session row and an audit row each time.
    /// </summary>
    /// <returns>The rule.</returns>
    public static DemoEndpointRule AllowSignIn()
    {
        return new DemoEndpointRule(DemoAccess.Allowed, null, null, false, false, true);
    }

    /// <summary>The call runs unless the resource named by a route value is protected.</summary>
    /// <param name="routeKey">The route value holding the resource id, such as <c>id</c>.</param>
    /// <returns>The rule.</returns>
    public static DemoEndpointRule AllowUnlessProtected(string routeKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeKey);
        return new DemoEndpointRule(DemoAccess.AllowedUnlessProtected, null, routeKey, true, true);
    }

    /// <summary>The call is refused, and the area says why.</summary>
    /// <param name="area">The area that explains the refusal.</param>
    /// <returns>The rule.</returns>
    public static DemoEndpointRule Refuse(DemoRefusalArea area)
    {
        return new DemoEndpointRule(DemoAccess.Refused, area, null, false, false);
    }
}
