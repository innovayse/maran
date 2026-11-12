using System.Reflection;
using Maran.Host.Demo.Guard;
using Microsoft.AspNetCore.Mvc;

namespace Maran.ArchitectureTests;

/// <summary>
/// Holds the public demo's free-text walk against the composed controllers: no screened action may take an
/// argument whose text sits deeper than the walk reads, or in a type that contains itself.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a census.</b> The walk refuses at its depth cap at run time (<see cref="DemoFreeText.MaximumDepth"/>), so a
/// command nested deeper would be refused for every visitor, whatever they typed. The census turns that into a
/// build failure for whoever adds the command, who is told to flatten it or raise the cap on purpose.
/// </para>
/// <para>
/// Only the actions the demo table screens count (<see cref="Maran.Host.Demo.Guard.DemoEndpointRule.ScreensText"/>):
/// a read, a refused action and a sign-in never reach the walk.
/// </para>
/// </remarks>
public static class DemoFreeTextCensus
{
    /// <summary>Every argument of every screened mutating action that can hold text, with its depth.</summary>
    /// <param name="controllers">The controller types to inspect.</param>
    /// <param name="policy">The demo's table, which says which actions are screened.</param>
    /// <returns>One entry per argument: the action's name, the parameter and the deepest text depth below it.</returns>
    public static List<(string Action, ParameterInfo Parameter, int Depth)> ScreenedArguments(
        IEnumerable<Type> controllers,
        DemoEndpointPolicy policy)
    {
        var arguments = new List<(string, ParameterInfo, int)>();
        foreach (var controller in controllers.OrderBy(type => { return type.Name; }, StringComparer.Ordinal))
        {
            foreach (var action in ControllerAuthorizationCensus.ActionsOf(controller).Where(DemoRefusalCensus.IsMutation))
            {
                var rule = policy.RuleFor(DemoEndpointPolicy.ActionIdOf(action));
                if (rule is null || !rule.ScreensText)
                {
                    continue;
                }

                foreach (var parameter in action.GetParameters().Where(IsBound))
                {
                    var depth = DemoFreeText.DeepestTextOf(parameter.ParameterType);
                    if (depth >= 0)
                    {
                        arguments.Add((ControllerAuthorizationCensus.NameOf(action), parameter, depth));
                    }
                }
            }
        }

        return arguments;
    }

    /// <summary>Runs the rule over a set of controllers.</summary>
    /// <param name="controllers">The controller types to inspect.</param>
    /// <param name="policy">The demo's table.</param>
    /// <returns>One line per argument whose text the walk would refuse.</returns>
    public static List<string> FindViolations(IEnumerable<Type> controllers, DemoEndpointPolicy policy)
    {
        return ScreenedArguments(controllers, policy)
            .Where(argument => { return argument.Depth > DemoFreeText.MaximumDepth; })
            .Select(argument =>
            {
                var where = argument.Depth == int.MaxValue
                    ? "in a type that contains itself"
                    : $"{argument.Depth} levels deep";
                return $"{argument.Action} parameter '{argument.Parameter.Name}' holds text {where}";
            })
            .ToList();
    }

    /// <summary>Whether a parameter is bound from the request and so can carry a visitor's text.</summary>
    /// <param name="parameter">The parameter.</param>
    /// <returns><c>false</c> for a cancellation token and a service.</returns>
    private static bool IsBound(ParameterInfo parameter)
    {
        return parameter.ParameterType != typeof(CancellationToken)
            && parameter.GetCustomAttribute<FromServicesAttribute>() is null;
    }
}
