using System.Reflection;
using Maran.Host.Demo.Guard;
using Maran.Host.Demo.Guard.Enums;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Maran.ArchitectureTests;

/// <summary>
/// Reads the composed controllers' metadata and holds the public demo's refusal table against it: every
/// action that changes things is classified, every table entry still names such an action, and the
/// classification is consistent with the action's route and anonymity (issue #95).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a census as well as a default.</b> The guard refuses an unclassified mutating endpoint at run time,
/// so a forgotten entry fails closed. The census makes the closed door a reviewed decision: whoever adds
/// an endpoint is told, in CI, to write down what a visitor of the public demo may do there. A marketplace
/// module's controller is caught the same way when it is composed here.
/// </para>
/// <para>
/// UNOBSERVED HERE: minimal endpoints, which are not controller actions. The guard names the one it allows
/// (<see cref="DemoMinimalEndpointPolicy"/>) and refuses the rest at run time; the demo integration tests
/// read the real endpoint data source.
/// </para>
/// </remarks>
public static class DemoRefusalCensus
{
    /// <summary>Whether an action answers any method that changes things.</summary>
    /// <param name="action">The action method.</param>
    /// <returns><c>true</c> unless it answers only GET and HEAD.</returns>
    public static bool IsMutation(MethodInfo action)
    {
        return !ViewAsAccessCensus.AnswersOnlySafeMethods(action);
    }

    /// <summary>Runs the rules over a set of controllers.</summary>
    /// <param name="controllers">The controller types to inspect.</param>
    /// <param name="policy">The table to hold against them.</param>
    /// <returns>One ordered line per violation, each beginning with the action's name.</returns>
    public static List<string> FindViolations(IEnumerable<Type> controllers, DemoEndpointPolicy policy)
    {
        var violations = new List<string>();

        foreach (var controller in controllers.OrderBy(type => { return type.Name; }, StringComparer.Ordinal))
        {
            foreach (var action in ControllerAuthorizationCensus.ActionsOf(controller).Where(IsMutation))
            {
                var name = ControllerAuthorizationCensus.NameOf(action);
                var rule = policy.RuleFor(DemoEndpointPolicy.ActionIdOf(action));

                if (rule is null)
                {
                    violations.Add($"{name} is not classified in DemoEndpointTable");
                    continue;
                }

                if (rule.Access == DemoAccess.AllowedUnlessProtected && !RouteCarries(controller, action, rule.ProtectedRouteKey!))
                {
                    violations.Add($"{name} checks route value '{rule.ProtectedRouteKey}' that its route does not carry");
                }

                if (ControllerAuthorizationCensus.IsAnonymous(action)
                    && rule.Access != DemoAccess.Refused
                    && rule.CountsTowardBudget)
                {
                    violations.Add($"{name} is anonymous and allowed but would spend the shared budget");
                }
            }
        }

        return violations;
    }

    /// <summary>Finds table entries that no longer name a mutating action.</summary>
    /// <param name="controllers">The controller types the table is held against.</param>
    /// <param name="policy">The table.</param>
    /// <returns>One line per entry naming no mutating action.</returns>
    public static List<string> FindStaleEntries(IEnumerable<Type> controllers, DemoEndpointPolicy policy)
    {
        var mutating = controllers
            .SelectMany(controller => { return ControllerAuthorizationCensus.ActionsOf(controller); })
            .Where(IsMutation)
            .Select(DemoEndpointPolicy.ActionIdOf)
            .ToHashSet(StringComparer.Ordinal);

        return policy.ActionIds
            .Where(id => { return !mutating.Contains(id); })
            .Order(StringComparer.Ordinal)
            .Select(id => { return $"{id} names no mutating action"; })
            .ToList();
    }

    /// <summary>Whether an action's route (controller templates plus its own) carries a route value.</summary>
    /// <param name="controller">The controller declaring the action.</param>
    /// <param name="action">The action method.</param>
    /// <param name="key">The route value name.</param>
    /// <returns><c>true</c> when some template contains <c>{key</c> followed by a closing brace or a constraint.</returns>
    public static bool RouteCarries(Type controller, MethodInfo action, string key)
    {
        return controller.GetCustomAttributes(true).Concat(action.GetCustomAttributes(true))
            .OfType<IRouteTemplateProvider>()
            .Select(provider => { return provider.Template ?? string.Empty; })
            .Any(template =>
            {
                return template.Contains("{" + key + "}", StringComparison.Ordinal)
                    || template.Contains("{" + key + ":", StringComparison.Ordinal);
            });
    }
}
