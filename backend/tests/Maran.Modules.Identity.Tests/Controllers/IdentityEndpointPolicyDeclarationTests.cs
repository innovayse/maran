using System.Reflection;
using Maran.Modules.Identity.Controllers;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Maran.Modules.Identity.Tests.Controllers;

/// <summary>
/// Pins how the Identity controllers declare who may call them, now that the panel's fallback
/// policy is administrator-only: a customer-level action must name its policy, because an action
/// that names nothing falls to the fallback and a customer is refused.
/// </summary>
/// <remarks>
/// Which actions may be anonymous is decided once, in the architecture tests' anonymous-action pin
/// (<c>AnonymousActionPin</c>); this class does not repeat that list. It holds what the pin cannot
/// see: that the customer-level actions of this module name the customer-level policy.
/// </remarks>
public sealed class IdentityEndpointPolicyDeclarationTests
{
    /// <summary>The Identity controllers that declare authorization on specific actions.</summary>
    private static readonly Type[] PerActionControllers = [typeof(AuthController), typeof(SetupController)];

    /// <summary>Guards the walk against going blind: the controllers it should see are seen.</summary>
    [Fact]
    public void The_walk_sees_the_actions_of_the_identity_controllers()
    {
        var names = AllActions().Select(NameOf).ToHashSet();

        Assert.Contains("SessionsController.GetAllAsync", names);
        Assert.Contains("AuthController.LogoutEverywhereAsync", names);
        Assert.Contains("SetupController.GetStateAsync", names);
        Assert.True(names.Count > 15, $"Only {names.Count} actions were found.");
    }

    /// <summary>A class-level anonymous grant outranks any action-level policy, so none is allowed.</summary>
    [Fact]
    public void No_identity_controller_grants_anonymity_on_the_class()
    {
        var offenders = typeof(AuthController).Assembly
            .GetTypes()
            .Where(type =>
            {
                return typeof(ControllerBase).IsAssignableFrom(type);
            })
            .Where(type =>
            {
                return type.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();
            })
            .Select(type =>
            {
                return type.Name;
            })
            .ToArray();

        Assert.Empty(offenders);
    }

    /// <summary>Every action is either anonymous or carries a named, known policy.</summary>
    [Fact]
    public void Every_identity_action_is_anonymous_or_names_a_known_policy()
    {
        var known = new[] { AuthorizationPolicies.AdminOnly, AuthorizationPolicies.AnyAuthenticated };
        var violations = new List<string>();

        foreach (var method in AllActions())
        {
            var name = NameOf(method);
            var anonymous = method.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();
            if (anonymous)
            {
                continue;
            }

            var policies = PoliciesOf(method);
            if (policies.Length == 0 || policies.Any(policy =>
            {
                return !known.Contains(policy);
            }))
            {
                violations.Add($"{name} names no known policy ({string.Join(",", policies)})");
            }
        }

        Assert.Empty(violations);
    }

    /// <summary>Each per-action controller grants anonymity on its actions, not on the class.</summary>
    [Fact]
    public void The_per_action_controllers_carry_anonymity_on_their_actions()
    {
        foreach (var controller in PerActionControllers)
        {
            Assert.Empty(controller.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
            Assert.Contains(
                ActionsOf(controller),
                method =>
                {
                    return method.GetCustomAttributes<AllowAnonymousAttribute>().Any();
                });
        }
    }

    /// <summary>The customer-level actions name the customer-level policy, not the admin one.</summary>
    /// <param name="name">The action as "Controller.Method".</param>
    [Theory]
    [InlineData("SessionsController.GetAllAsync")]
    [InlineData("AuthController.LogoutEverywhereAsync")]
    public void A_customer_level_action_names_the_any_authenticated_policy(string name)
    {
        var method = AllActions().SingleOrDefault(candidate =>
        {
            return NameOf(candidate) == name;
        });

        Assert.NotNull(method);
        Assert.Equal(new[] { AuthorizationPolicies.AnyAuthenticated }, PoliciesOf(method!));
    }

    /// <summary>The three two-factor actions name the customer-level policy.</summary>
    [Fact]
    public void The_two_factor_management_actions_name_the_any_authenticated_policy()
    {
        var twoFactor = ActionsOf(typeof(AuthController))
            .Where(method =>
            {
                return method.GetCustomAttributes<HttpPostAttribute>()
                                .Any(post =>
                                {
                                    return post.Template?.StartsWith("two-factor/", StringComparison.Ordinal) == true;
                                });
            })
            .ToArray();

        Assert.Equal(3, twoFactor.Length);
        Assert.All(
            twoFactor,
            method =>
            {
                Assert.Equal(new[] { AuthorizationPolicies.AnyAuthenticated }, PoliciesOf(method));
            });
    }

    /// <summary>Public methods of a controller that carry an HTTP verb attribute.</summary>
    /// <param name="controller">The controller type.</param>
    /// <returns>The action methods.</returns>
    private static IEnumerable<MethodInfo> ActionsOf(Type controller)
    {
        return controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method =>
            {
                return method.GetCustomAttributes<HttpMethodAttribute>().Any();
            });
    }

    /// <summary>Every action of every Identity controller.</summary>
    /// <returns>The action methods.</returns>
    private static IEnumerable<MethodInfo> AllActions()
    {
        return typeof(AuthController).Assembly
            .GetTypes()
            .Where(type =>
            {
                return typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract;
            })
            .SelectMany(ActionsOf);
    }

    /// <summary>The "Controller.Method" name an action is listed under.</summary>
    /// <param name="method">The action.</param>
    /// <returns>The display name.</returns>
    private static string NameOf(MethodInfo method)
    {
        return $"{method.DeclaringType!.Name}.{method.Name}";
    }

    /// <summary>The policies an action names, from its own attributes and its class's.</summary>
    /// <param name="method">The action.</param>
    /// <returns>The policy names, empty strings for a bare declaration.</returns>
    private static string[] PoliciesOf(MethodInfo method)
    {
        return method.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Concat(method.DeclaringType!.GetCustomAttributes<AuthorizeAttribute>(inherit: true))
            .Select(attribute =>
            {
                return attribute.Policy ?? string.Empty;
            })
            .ToArray();
    }
}
