using System.Reflection;
using Maran.ArchitectureTests.Fixtures;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Maran.ArchitectureTests;

/// <summary>
/// Reads, from the composed controllers' metadata, whether each action a view-as token can reach is
/// classified with <see cref="ViewAsAccessAttribute"/>, and reports every action that is not or that is
/// classified in a way the handler would not honour (issue #334).
/// </summary>
/// <remarks>
/// <para>
/// <b>The rule.</b> View-as is an allow-list: an undeclared endpoint is refused at run time, so a
/// forgotten declaration fails closed. This census exists so that closed is also a reviewed decision —
/// every action says what a view-as token may do there, and the refusals are written down as
/// <see cref="ViewAsAccess.Refused"/> rather than inferred from silence. A safe mutation must also be
/// listed in <see cref="ViewAsSafeMutationPin"/> with its reason.
/// </para>
/// <para>
/// <b>Which actions it asks about.</b> The token a view-as session issues carries the customer role and
/// the customer's account, so an action that also names the administrator policy is unreachable to it,
/// and an anonymous action reads no token. Everything else is asked.
/// </para>
/// <para>
/// <b>What it cannot see.</b> Minimal endpoints (<c>/api/v1/dashboard</c>) are not controller actions;
/// the Host integration tests read the real endpoint data source for those.
/// </para>
/// </remarks>
public static class ViewAsAccessCensus
{
    /// <summary>Whether a view-as token can reach the action, so that it must be classified.</summary>
    /// <param name="controller">The controller declaring the action.</param>
    /// <param name="action">The action method.</param>
    /// <returns><c>false</c> for an anonymous action and for one that also names the administrator policy.</returns>
    public static bool IsReachableByViewAsToken(Type controller, MethodInfo action)
    {
        if (ControllerAuthorizationCensus.IsAnonymous(action))
        {
            return false;
        }

        return !controller.GetCustomAttributes(true).OfType<IAuthorizeData>()
            .Concat(action.GetCustomAttributes(true).OfType<IAuthorizeData>())
            .Any(data => { return data.Policy == AuthorizationPolicies.AdminOnly; });
    }

    /// <summary>The access an action declares: its own, else its controller's.</summary>
    /// <param name="controller">The controller declaring the action.</param>
    /// <param name="action">The action method.</param>
    /// <returns>The declared access, or <c>null</c> when neither declares one.</returns>
    public static ViewAsAccess? DeclaredAccess(Type controller, MethodInfo action)
    {
        var own = action.GetCustomAttribute<ViewAsAccessAttribute>(inherit: true);
        return (own ?? controller.GetCustomAttribute<ViewAsAccessAttribute>(inherit: true))?.Access;
    }

    /// <summary>Whether every HTTP method an action answers is a safe one (GET or HEAD).</summary>
    /// <param name="action">The action method.</param>
    /// <returns><c>true</c> when the action answers nothing but GET and HEAD.</returns>
    public static bool AnswersOnlySafeMethods(MethodInfo action)
    {
        return action.GetCustomAttributes(true).OfType<IActionHttpMethodProvider>()
            .SelectMany(provider => { return provider.HttpMethods; })
            .All(method => { return HttpMethods.IsGet(method) || HttpMethods.IsHead(method); });
    }

    /// <summary>Runs the rule over a set of controllers.</summary>
    /// <param name="controllers">The controller types to inspect.</param>
    /// <param name="pin">The safe mutations that are allowed to be declared.</param>
    /// <returns>One ordered line per violation, each beginning with the action's name.</returns>
    public static List<string> FindViolations(
        IEnumerable<Type> controllers,
        IReadOnlyCollection<PinnedSafeMutation> pin)
    {
        var pinned = new HashSet<string>(pin.Select(entry => { return entry.Action; }), StringComparer.Ordinal);
        var violations = new List<string>();

        foreach (var controller in controllers.OrderBy(type => { return type.Name; }, StringComparer.Ordinal))
        {
            foreach (var action in ControllerAuthorizationCensus.ActionsOf(controller))
            {
                if (!IsReachableByViewAsToken(controller, action))
                {
                    continue;
                }

                var name = ControllerAuthorizationCensus.NameOf(action);
                var access = DeclaredAccess(controller, action);
                var safeOnly = AnswersOnlySafeMethods(action);

                if (access is null)
                {
                    violations.Add($"{name} declares no ViewAsAccess");
                }
                else if (access == ViewAsAccess.Read && !safeOnly)
                {
                    violations.Add($"{name} is declared a Read but answers a method that changes things");
                }
                else if (access == ViewAsAccess.SafeMutation && safeOnly)
                {
                    violations.Add($"{name} is declared a SafeMutation but only reads; declare it a Read");
                }
                else if (access == ViewAsAccess.SafeMutation && !pinned.Contains(name))
                {
                    violations.Add($"{name} is a SafeMutation but is not in ViewAsSafeMutationPin");
                }
            }
        }

        return violations;
    }

    /// <summary>Finds pin entries that no longer describe a safe-mutation action.</summary>
    /// <param name="controllers">The controller types the pin is held against.</param>
    /// <param name="pin">The pinned safe mutations.</param>
    /// <returns>One line per entry naming no action, naming one that is not a safe mutation, or lacking a reason.</returns>
    public static List<string> FindStalePins(
        IEnumerable<Type> controllers,
        IReadOnlyCollection<PinnedSafeMutation> pin)
    {
        var actions = controllers
            .SelectMany(controller =>
            {
                return ControllerAuthorizationCensus.ActionsOf(controller)
                    .Select(action => { return (Controller: controller, Action: action); });
            })
            .ToDictionary(
                pair => { return ControllerAuthorizationCensus.NameOf(pair.Action); },
                pair => { return pair; },
                StringComparer.Ordinal);
        var stale = new List<string>();

        foreach (var entry in pin)
        {
            if (string.IsNullOrWhiteSpace(entry.Reason))
            {
                stale.Add($"{entry.Action} has no reason");
            }

            if (!actions.TryGetValue(entry.Action, out var pair))
            {
                stale.Add($"{entry.Action} names no action");
            }
            else if (DeclaredAccess(pair.Controller, pair.Action) != ViewAsAccess.SafeMutation)
            {
                stale.Add($"{entry.Action} is pinned as a safe mutation but is not declared one");
            }
        }

        return stale;
    }
}
