using Maran.ArchitectureTests.Fixtures;
using Maran.Modules.Identity.Controllers;
using Microsoft.AspNetCore.RateLimiting;

namespace Maran.ArchitectureTests;

/// <summary>
/// Holds the rule that every anonymous action of the authentication controller names a rate-limit
/// policy (rules/csharp.md "Rate limiting is mandatory on authentication"; issue #471).
/// </summary>
/// <remarks>
/// <para>
/// <b>What it cannot see.</b> It reads attributes, so a limit applied by route-group metadata or a
/// global limiter is invisible to it; the panel has neither, and the Host integration tests
/// (<c>SessionRefreshRateLimitTests</c>) measure the 429 on the wire.
/// </para>
/// <para>
/// Held by a planted controller it must report and an accepting one it must not.
/// </para>
/// </remarks>
public sealed class AnonymousAuthRateLimitTests
{
    /// <summary>Every anonymous action of <see cref="AuthController"/> names a rate-limit policy.</summary>
    [Fact]
    public void Every_anonymous_action_of_the_authentication_controller_names_a_rate_limit_policy()
    {
        var offenders = FindUnlimitedAnonymousActions(typeof(AuthController));

        Assert.True(
            offenders.Count == 0,
            string.Join("; ", offenders) + ". Add [EnableRateLimiting(RateLimitPolicies.<Name>)].");
    }

    /// <summary>The walk is not empty, so "no offenders" is not the answer of a blind walk.</summary>
    [Fact]
    public void The_walk_covers_the_refresh_and_logout_actions()
    {
        var anonymous = ControllerAuthorizationCensus.ActionsOf(typeof(AuthController))
            .Where(action => { return ControllerAuthorizationCensus.IsAnonymous(action); })
            .Select(action => { return action.Name; })
            .ToList();

        Assert.Contains(nameof(AuthController.RefreshAsync), anonymous);
        Assert.Contains(nameof(AuthController.LogoutAsync), anonymous);
    }

    /// <summary>The check reports an anonymous action that names no policy.</summary>
    [Fact]
    public void The_check_reports_a_planted_unlimited_anonymous_action()
    {
        var offenders = FindUnlimitedAnonymousActions(typeof(UnlimitedAnonymousAuthController));

        Assert.Equal(new List<string> { "UnlimitedAnonymousAuthController.RefreshAsync" }, offenders);
    }

    /// <summary>The check leaves an anonymous action that names a policy alone.</summary>
    [Fact]
    public void The_check_accepts_an_anonymous_action_that_names_a_policy()
    {
        Assert.Empty(FindUnlimitedAnonymousActions(typeof(LimitedAnonymousAuthController)));
    }

    /// <summary>Lists the anonymous actions of a controller with no <c>[EnableRateLimiting]</c>.</summary>
    /// <param name="controller">The controller to inspect.</param>
    /// <returns>One <c>Controller.Action</c> line per offender.</returns>
    private static List<string> FindUnlimitedAnonymousActions(Type controller)
    {
        return ControllerAuthorizationCensus.ActionsOf(controller)
            .Where(action => { return ControllerAuthorizationCensus.IsAnonymous(action); })
            .Where(action =>
            {
                return !action.GetCustomAttributes(true).OfType<EnableRateLimitingAttribute>().Any()
                    && !controller.GetCustomAttributes(true).OfType<EnableRateLimitingAttribute>().Any();
            })
            .Select(ControllerAuthorizationCensus.NameOf)
            .ToList();
    }
}
