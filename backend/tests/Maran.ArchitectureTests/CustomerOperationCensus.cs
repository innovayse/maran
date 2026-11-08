using System.Reflection;
using System.Text.RegularExpressions;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Maran.ArchitectureTests;

/// <summary>
/// Reads, from the composed controllers' metadata, every operation a customer's token can reach, so
/// that the customer self-service matrix is a checked record and not a document that drifts (issue #147).
/// </summary>
/// <remarks>
/// <para>
/// <b>Which actions a customer reaches.</b> An action is customer-reachable when it is not anonymous
/// (an anonymous action reads no token, so it is no operation of a customer's) and every
/// <c>[Authorize]</c> that applies to it, its class's and its own, names
/// <see cref="AuthorizationPolicies.AnyAuthenticated"/>. Authorization attributes are ANDed, so one
/// administrator-only, reseller or API-key attribute anywhere makes the action unreachable to a customer's
/// token. This is the same "who is admitted" reading <see cref="ViewAsAccessCensus"/> makes, from the other
/// side: that census asks what a viewing operator may do, this one asks what the customer may do.
/// </para>
/// <para>
/// <b>What it cannot see.</b> Minimal endpoints (<c>/api/v1/dashboard</c>) are not controller actions; a
/// plan feature that withholds an action from a customer (<c>RequiresAccountFeature</c>) is runtime
/// state, so a listed operation may still answer a given customer 403; and the census reads which
/// operations exist, never what any of them does.
/// </para>
/// </remarks>
public static class CustomerOperationCensus
{
    /// <summary>The prefix every module assembly name carries.</summary>
    private const string ModuleAssemblyPrefix = "Maran.Modules.";

    /// <summary>The placeholder in a route template that stands for the controller's name.</summary>
    private const string ControllerToken = "[controller]";

    /// <summary>Whether a customer's token can reach the action.</summary>
    /// <param name="controller">The controller declaring the action.</param>
    /// <param name="action">The action method.</param>
    /// <returns><c>true</c> for a non-anonymous action whose every authorization names the any-authenticated policy.</returns>
    public static bool IsReachableByCustomer(Type controller, MethodInfo action)
    {
        if (ControllerAuthorizationCensus.IsAnonymous(action))
        {
            return false;
        }

        var data = controller.GetCustomAttributes(true).OfType<IAuthorizeData>()
            .Concat(action.GetCustomAttributes(true).OfType<IAuthorizeData>())
            .ToList();

        return data.Count > 0
            && data.All(item =>
            {
                return item.Policy == AuthorizationPolicies.AnyAuthenticated
                    && string.IsNullOrWhiteSpace(item.Roles)
                    && string.IsNullOrWhiteSpace(item.AuthenticationSchemes);
            });
    }

    /// <summary>Every operation of a set of controllers that a customer's token can reach.</summary>
    /// <param name="controllers">The controller types to walk.</param>
    /// <returns>One entry per HTTP method of each reachable action, ordered by key.</returns>
    public static List<CustomerOperation> OperationsOf(IEnumerable<Type> controllers)
    {
        var operations = new List<CustomerOperation>();

        foreach (var controller in controllers)
        {
            foreach (var action in ControllerAuthorizationCensus.ActionsOf(controller))
            {
                if (!IsReachableByCustomer(controller, action))
                {
                    continue;
                }

                foreach (var (method, route) in RoutesOf(controller, action))
                {
                    operations.Add(new CustomerOperation(
                        method,
                        route,
                        ModuleOf(controller),
                        ControllerAuthorizationCensus.NameOf(action)));
                }
            }
        }

        return operations.OrderBy(operation => { return operation.Key; }, StringComparer.Ordinal).ToList();
    }

    /// <summary>Lists the operations a customer's token can reach that the matrix does not name.</summary>
    /// <param name="operations">The operations the census found.</param>
    /// <param name="matrix">The matrix entries.</param>
    /// <returns>One line per unlisted operation, naming its action.</returns>
    public static List<string> FindUnlisted(
        IEnumerable<CustomerOperation> operations,
        IReadOnlyCollection<CustomerOperationEntry> matrix)
    {
        var listed = new HashSet<string>(matrix.Select(entry => { return entry.Key; }), StringComparer.Ordinal);

        return operations
            .Where(operation => { return !listed.Contains(operation.Key); })
            .Select(operation => { return $"{operation.Key} ({operation.Action}) is not in the matrix"; })
            .ToList();
    }

    /// <summary>Lists the matrix entries that name an operation no longer reachable by a customer.</summary>
    /// <param name="operations">The operations the census found.</param>
    /// <param name="matrix">The matrix entries.</param>
    /// <returns>One line per stale entry.</returns>
    public static List<string> FindStale(
        IEnumerable<CustomerOperation> operations,
        IReadOnlyCollection<CustomerOperationEntry> matrix)
    {
        var found = new HashSet<string>(operations.Select(operation => { return operation.Key; }), StringComparer.Ordinal);

        return matrix
            .Where(entry => { return !found.Contains(entry.Key); })
            .Select(entry => { return $"{entry.Key} is in the matrix but names no customer-reachable operation"; })
            .ToList();
    }

    /// <summary>Lists the entries whose module is not the one that serves the operation, or whose isolation proof does not exist.</summary>
    /// <param name="operations">The operations the census found.</param>
    /// <param name="matrix">The matrix entries.</param>
    /// <param name="testSources">The text of every source file of the integration tests.</param>
    /// <returns>One line per entry whose recorded module differs, whose class has no source, or whose exemption has no reason.</returns>
    public static List<string> FindUnproven(
        IEnumerable<CustomerOperation> operations,
        IReadOnlyCollection<CustomerOperationEntry> matrix,
        IReadOnlyCollection<string> testSources)
    {
        var byKey = operations.ToDictionary(operation => { return operation.Key; }, StringComparer.Ordinal);
        var problems = new List<string>();

        foreach (var entry in matrix)
        {
            if (byKey.TryGetValue(entry.Key, out var operation)
                && !string.Equals(operation.Module, entry.Module, StringComparison.Ordinal))
            {
                problems.Add($"{entry.Key} is served by module {operation.Module}, not {entry.Module}");
            }

            if (entry.IsolationClass is null)
            {
                if (string.IsNullOrWhiteSpace(entry.Reason))
                {
                    problems.Add($"{entry.Key} names no isolation test class and gives no reason");
                }

                continue;
            }

            if (!testSources.Any(source => { return DeclaresClass(source, entry.IsolationClass); }))
            {
                problems.Add($"{entry.Key} names {entry.IsolationClass}, which no integration test file declares");
            }
        }

        return problems;
    }

    /// <summary>Whether a source file declares a type with the given name.</summary>
    /// <param name="source">The file's text.</param>
    /// <param name="name">The type name.</param>
    /// <returns><c>true</c> when a <c>class</c> or <c>record</c> declaration of exactly that name occurs.</returns>
    private static bool DeclaresClass(string source, string name)
    {
        return Regex.IsMatch(
            source,
            $@"\b(class|record)\s+{Regex.Escape(name)}\b");
    }

    /// <summary>The module that serves a controller.</summary>
    /// <param name="controller">The controller type.</param>
    /// <returns>The assembly name without its <c>Maran.Modules.</c> prefix, or the whole name for another assembly.</returns>
    private static string ModuleOf(Type controller)
    {
        var name = controller.Assembly.GetName().Name ?? string.Empty;
        return name.StartsWith(ModuleAssemblyPrefix, StringComparison.Ordinal) ? name[ModuleAssemblyPrefix.Length..] : name;
    }

    /// <summary>
    /// The method and full route of each HTTP verb attribute on an action, under each route the nearest
    /// routed class in the controller's hierarchy declares.
    /// </summary>
    /// <param name="controller">The controller declaring the action.</param>
    /// <param name="action">The action method.</param>
    /// <returns>One pair per verb.</returns>
    private static IEnumerable<(string Method, string Route)> RoutesOf(Type controller, MethodInfo action)
    {
        var routed = controller;
        while (routed is not null && !routed.GetCustomAttributes<RouteAttribute>(inherit: false).Any())
        {
            routed = routed.BaseType;
        }

        var controllerName = controller.Name.EndsWith("Controller", StringComparison.Ordinal)
            ? controller.Name[..^"Controller".Length]
            : controller.Name;
        var prefixes = (routed?.GetCustomAttributes<RouteAttribute>(inherit: false) ?? Enumerable.Empty<RouteAttribute>())
            .Select(attribute => { return attribute.Template.Replace(ControllerToken, controllerName, StringComparison.Ordinal); })
            .ToList();

        foreach (var prefix in prefixes)
        {
            foreach (var provider in action.GetCustomAttributes(true).OfType<IActionHttpMethodProvider>())
            {
                var template = (provider as IRouteTemplateProvider)?.Template ?? string.Empty;
                var route = template.Length == 0 ? prefix : $"{prefix}/{template}".Trim('/');

                foreach (var method in provider.HttpMethods)
                {
                    yield return (method.ToUpperInvariant(), route);
                }
            }
        }
    }
}
