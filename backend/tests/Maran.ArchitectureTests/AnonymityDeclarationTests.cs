using Maran.ArchitectureTests.Fixtures;
using Maran.Modules.Identity.Controllers;

namespace Maran.ArchitectureTests;

/// <summary>
/// Holds the rule that anonymity is granted per action and never on a controller class.
/// </summary>
/// <remarks>
/// <para>
/// A class-level <c>[AllowAnonymous]</c> outranks every action-level <c>[Authorize]</c> under it,
/// and MVC copies class and action attributes into the same endpoint metadata, so the route-table
/// census cannot tell a class-level grant from an action-level one. A new action added beneath such
/// a class would be open to the world with no attribute of its own to review. This reads the
/// controllers' types instead, backed by <see cref="ControllerAuthorizationCensus"/>.
/// </para>
/// <para>
/// The check is held by a planted controller it must report and by the two real controllers that
/// are anonymous per action and must not be reported.
/// </para>
/// </remarks>
public sealed class AnonymityDeclarationTests
{
    /// <summary>No composed controller grants anonymity on the class.</summary>
    [Fact]
    public void No_composed_controller_grants_anonymity_at_class_level()
    {
        var offenders = ControllerAuthorizationCensus.FindClassLevelAnonymity(
            ControllerAuthorizationCensus.ComposedControllers());

        Assert.True(
            offenders.Count == 0,
            string.Join("; ", offenders) + ". Move [AllowAnonymous] onto the individual actions and pin them "
            + "in AnonymousActionPin.");
    }

    /// <summary>The walk is not empty, so "no offenders" is not the answer of a blind walk.</summary>
    [Fact]
    public void The_class_level_walk_covers_the_controllers_that_are_anonymous_per_action()
    {
        var composed = ControllerAuthorizationCensus.ComposedControllers();

        Assert.Contains(typeof(SetupController), composed);
        Assert.Contains(typeof(AuthController), composed);
    }

    /// <summary>The check reports a controller that carries the attribute on its class.</summary>
    /// <remarks>The positive control: the real walk is satisfied by a check that always passes.</remarks>
    [Fact]
    public void The_class_level_anonymity_check_reports_a_planted_controller()
    {
        var offenders = ControllerAuthorizationCensus.FindClassLevelAnonymity(
            new[] { typeof(ClassLevelAnonymousController) });

        Assert.Equal(
            new List<string> { "ClassLevelAnonymousController grants anonymity at class level" },
            offenders);
    }

    /// <summary>The check leaves alone controllers whose anonymity is declared per action.</summary>
    /// <remarks>
    /// The inverse control, run over the two real controllers that have anonymous actions: a check
    /// that also inspected methods would refuse the sign-in and first-run screens.
    /// </remarks>
    [Fact]
    public void The_class_level_anonymity_check_accepts_action_level_anonymity()
    {
        var controllers = new[] { typeof(SetupController), typeof(AuthController) };

        var anonymousActions = controllers
            .SelectMany(controller => { return ControllerAuthorizationCensus.ActionsOf(controller); })
            .Where(action => { return ControllerAuthorizationCensus.IsAnonymous(action); })
            .ToList();

        // Precondition: there IS action-level anonymity for the check to leave alone.
        Assert.NotEmpty(anonymousActions);
        Assert.Empty(ControllerAuthorizationCensus.FindClassLevelAnonymity(controllers));
    }
}
