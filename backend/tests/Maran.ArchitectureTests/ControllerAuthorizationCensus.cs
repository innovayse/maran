using System.Reflection;
using Maran.ArchitectureTests.Fixtures;
using Maran.Host.Modules;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Maran.ArchitectureTests;

/// <summary>
/// Reads, from the composed controllers' metadata, how each action declares who may call it, and
/// reports every action that declares it in a way the panel does not allow.
/// </summary>
/// <remarks>
/// <para>
/// <b>The rule.</b> The panel's authorization fallback is administrator-only, so an action with no
/// metadata is not an open door — but it is not a reviewed decision either, and a marketplace module
/// is code this repository never tests. An action therefore either is anonymous and listed in
/// <see cref="AnonymousActionPin"/>, or carries at least one <c>[Authorize]</c> (its own or its
/// class's) and every one of them names <see cref="AuthorizationPolicies.AdminOnly"/> or
/// <see cref="AuthorizationPolicies.AnyAuthenticated"/> — no bare form, no <c>Roles</c>, no
/// authentication schemes, no unknown name.
/// </para>
/// <para>
/// <b>What it cannot see.</b> Minimal endpoints (<c>/health</c>, <c>/api/v1/modules</c>,
/// <c>/api/v1/dashboard</c>) are not controller actions, and a policy applied by a filter or by
/// route-group metadata is not an attribute. Those are held by the route-table census in the Host
/// integration tests, which reads the real endpoint data source.
/// </para>
/// </remarks>
public static class ControllerAuthorizationCensus
{
    /// <summary>The policy names the panel defines and an endpoint may name.</summary>
    private static readonly string[] KnownPolicies =
    {
        AuthorizationPolicies.AdminOnly,
        AuthorizationPolicies.AnyAuthenticated,
    };

    /// <summary>
    /// Every controller type of every module the Host composes, plus the Licensing module's, which the
    /// registry deliberately omits (no screen yet) but whose assembly is an application part of the Host,
    /// so its controller is routed all the same.
    /// </summary>
    /// <returns>The concrete controllers, ordered by full name.</returns>
    public static List<Type> ComposedControllers()
    {
        return ModuleRegistry.All
            .Select(module => { return module.GetType().Assembly; })
            .Append(typeof(Maran.Modules.Licensing.LicensingModule).Assembly)
            .Distinct()
            .SelectMany(assembly => { return assembly.GetTypes(); })
            .Where(IsController)
            .OrderBy(type => { return type.FullName; }, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Every controller type of every module assembly the build ships — a second source, independent
    /// of the hand-written module list, to hold the walk against.
    /// </summary>
    /// <returns>The concrete controllers, ordered by full name.</returns>
    public static List<Type> ShippedControllers()
    {
        return ModuleAssemblies.Modules()
            .SelectMany(assembly => { return assembly.GetTypes(); })
            .Where(IsController)
            .OrderBy(type => { return type.FullName; }, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The routable actions a controller declares itself.</summary>
    /// <param name="controller">The controller type.</param>
    /// <returns>The public instance methods carrying an HTTP verb attribute, ordered by name.</returns>
    public static List<MethodInfo> ActionsOf(Type controller)
    {
        return controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method =>
            {
                return method.GetCustomAttributes(true).OfType<IActionHttpMethodProvider>().Any()
                    && method.GetCustomAttribute<NonActionAttribute>() is null;
            })
            .OrderBy(method => { return method.Name; }, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The action's identity as the pin spells it.</summary>
    /// <param name="action">The action method.</param>
    /// <returns><c>ControllerType.MethodName</c>.</returns>
    public static string NameOf(MethodInfo action)
    {
        return $"{action.DeclaringType!.Name}.{action.Name}";
    }

    /// <summary>Whether an action is anonymous by its own attribute or its class's.</summary>
    /// <param name="action">The action method.</param>
    /// <returns><c>true</c> when an <see cref="IAllowAnonymous"/> applies.</returns>
    public static bool IsAnonymous(MethodInfo action)
    {
        return action.GetCustomAttributes(true).OfType<IAllowAnonymous>().Any()
            || action.DeclaringType!.GetCustomAttributes(true).OfType<IAllowAnonymous>().Any();
    }

    /// <summary>Runs the rule over a set of controllers.</summary>
    /// <param name="controllers">The controller types to inspect.</param>
    /// <param name="pin">The anonymous actions that are allowed to be anonymous.</param>
    /// <returns>One ordered line per violation, each beginning with the action's name.</returns>
    public static List<string> FindViolations(
        IEnumerable<Type> controllers,
        IReadOnlyCollection<PinnedAnonymousAction> pin)
    {
        var pinned = new HashSet<string>(pin.Select(entry => { return entry.Action; }), StringComparer.Ordinal);
        var violations = new List<string>();

        foreach (var controller in controllers.OrderBy(type => { return type.Name; }, StringComparer.Ordinal))
        {
            foreach (var action in ActionsOf(controller))
            {
                var name = NameOf(action);
                if (IsAnonymous(action))
                {
                    if (!pinned.Contains(name))
                    {
                        violations.Add($"{name} is anonymous but is not in AnonymousActionPin");
                    }

                    continue;
                }

                foreach (var problem in ProblemsOf(controller, action))
                {
                    violations.Add($"{name} {problem}");
                }
            }
        }

        return violations;
    }

    /// <summary>Finds pin entries that no longer describe an anonymous action.</summary>
    /// <param name="controllers">The controller types the pin is held against.</param>
    /// <param name="pin">The pinned anonymous actions.</param>
    /// <returns>One line per entry naming no action, naming a non-anonymous one, or lacking a reason.</returns>
    public static List<string> FindStalePins(
        IEnumerable<Type> controllers,
        IReadOnlyCollection<PinnedAnonymousAction> pin)
    {
        var actions = controllers
            .SelectMany(controller => { return ActionsOf(controller); })
            .ToDictionary(NameOf, action => { return action; }, StringComparer.Ordinal);
        var stale = new List<string>();

        foreach (var entry in pin)
        {
            if (string.IsNullOrWhiteSpace(entry.Reason))
            {
                stale.Add($"{entry.Action} has no reason");
            }

            if (!actions.TryGetValue(entry.Action, out var action))
            {
                stale.Add($"{entry.Action} names no action");
            }
            else if (!IsAnonymous(action))
            {
                stale.Add($"{entry.Action} is pinned as anonymous but is not");
            }
        }

        return stale;
    }

    /// <summary>Finds controllers that grant anonymity on the type instead of on an action.</summary>
    /// <param name="controllers">The controller types to inspect.</param>
    /// <returns>One line per controller carrying <see cref="IAllowAnonymous"/> on the type or a base type.</returns>
    public static List<string> FindClassLevelAnonymity(IEnumerable<Type> controllers)
    {
        return controllers
            .Where(controller => { return controller.GetCustomAttributes(true).OfType<IAllowAnonymous>().Any(); })
            .OrderBy(controller => { return controller.Name; }, StringComparer.Ordinal)
            .Select(controller => { return $"{controller.Name} grants anonymity at class level"; })
            .ToList();
    }

    /// <summary>Whether a type is a concrete MVC controller.</summary>
    /// <param name="type">The type to test.</param>
    /// <returns><c>true</c> for a non-abstract <see cref="ControllerBase"/> subclass.</returns>
    private static bool IsController(Type type)
    {
        return typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract;
    }

    /// <summary>Lists how a non-anonymous action fails to declare an allowed policy.</summary>
    /// <param name="controller">The controller declaring the action.</param>
    /// <param name="action">The action method.</param>
    /// <returns>Zero or more problem phrases, each completing "<c>Name ...</c>".</returns>
    private static List<string> ProblemsOf(Type controller, MethodInfo action)
    {
        var data = controller.GetCustomAttributes(true).OfType<IAuthorizeData>()
            .Concat(action.GetCustomAttributes(true).OfType<IAuthorizeData>())
            .ToList();

        if (data.Count == 0)
        {
            return new List<string> { "declares no authorization (it would fall to the fallback policy)" };
        }

        var problems = new List<string>();
        foreach (var item in data)
        {
            var hasRestriction = !string.IsNullOrWhiteSpace(item.Roles)
                || !string.IsNullOrWhiteSpace(item.AuthenticationSchemes);
            if (hasRestriction)
            {
                problems.Add("restricts by Roles or AuthenticationSchemes instead of a named policy");
            }

            if (string.IsNullOrWhiteSpace(item.Policy))
            {
                if (!hasRestriction)
                {
                    problems.Add("uses a bare [Authorize] that names no policy");
                }
            }
            else if (!KnownPolicies.Contains(item.Policy, StringComparer.Ordinal))
            {
                problems.Add($"names the unknown policy '{item.Policy}'");
            }
        }

        return problems.Distinct().ToList();
    }
}
