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
        Assert.Contains("ApiKeySelfController.GetAsync", names);
        Assert.Contains("ResellerApiKeysController.CreateAsync", names);
        Assert.Contains("ResellerApiKeyOversightController.RevokeAsync", names);
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
    /// <remarks>
    /// Known means one of the three JWT policies, or an API key policy declared together with the
    /// <c>ApiKey</c> scheme (<see cref="IsKnownDeclaration"/>).
    /// </remarks>
    [Fact]
    public void Every_identity_action_is_anonymous_or_names_a_known_policy()
    {
        var violations = new List<string>();

        foreach (var method in AllActions())
        {
            var name = NameOf(method);
            var anonymous = method.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();
            if (anonymous)
            {
                continue;
            }

            var declarations = AuthorizeAttributesOf(method);
            if (declarations.Length == 0 || declarations.Any(declaration =>
            {
                return !IsKnownDeclaration(declaration.Policy, declaration.AuthenticationSchemes);
            }))
            {
                violations.Add($"{name} names no known policy ({string.Join(",", PoliciesOf(method))})");
            }
        }

        Assert.Empty(violations);
    }

    /// <summary>
    /// An API key policy is known only together with the <c>ApiKey</c> scheme, and only when its name is
    /// a member of <see cref="ApiKeyPolicyNames"/>.
    /// </summary>
    /// <param name="policy">The policy an <c>[Authorize]</c> declares.</param>
    /// <param name="schemes">The authentication schemes the same declaration names.</param>
    /// <param name="expected">Whether the declaration is accepted.</param>
    [Theory]
    [InlineData(ApiKeyPolicyNames.SelfRead, ApiKeyDefaults.Scheme, true)]
    [InlineData(ApiKeyPolicyNames.ServerRead, ApiKeyDefaults.Scheme, true)]
    [InlineData(AuthorizationPolicies.ResellerOnly, null, true)]
    [InlineData(ApiKeyPolicyNames.SelfRead, null, false)]
    [InlineData(ApiKeyPolicyNames.SelfRead, "Bearer", false)]
    [InlineData("ApiKeyInvented", ApiKeyDefaults.Scheme, false)]
    [InlineData(null, ApiKeyDefaults.Scheme, false)]
    [InlineData(AuthorizationPolicies.AdminOnly, null, true)]
    [InlineData("Invented", null, false)]
    public void An_api_key_policy_is_known_only_together_with_the_api_key_scheme(
        string? policy,
        string? schemes,
        bool expected)
    {
        Assert.Equal(expected, IsKnownDeclaration(policy, schemes));
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

    /// <summary>
    /// Both view-as start endpoints are refused from inside a view-as session, and each names exactly
    /// the policy its edge allows: accounts for an administrator or a reseller, resellers for an
    /// administrator alone (issues #317, #179).
    /// </summary>
    /// <remarks>
    /// The refusal is what makes a view-as one hop: a token a view-as produced can reach neither start.
    /// The two policies are read from the actions themselves, so widening the reseller endpoint to
    /// <c>AdminOrReseller</c>, or narrowing the account one back, turns this red.
    /// </remarks>
    [Fact]
    public void The_two_view_as_start_endpoints_name_their_policy_and_refuse_a_view_as_token()
    {
        var starts = AllActions()
            .Where(method =>
            {
                return method.DeclaringType == typeof(ViewAsController)
                    && method.GetCustomAttributes<HttpPostAttribute>().Any();
            })
            .ToDictionary(NameOf);

        Assert.Equal(2, starts.Count);
        Assert.Equal(
            new[] { AuthorizationPolicies.AdminOrReseller },
            ActionPoliciesOf(starts["ViewAsController.StartAsync"]));
        Assert.Equal(
            new[] { AuthorizationPolicies.AdminOnly },
            ActionPoliciesOf(starts["ViewAsController.StartResellerAsync"]));
        Assert.All(
            starts.Values,
            method =>
            {
                Assert.Equal(
                    ViewAsAccess.Refused,
                    method.GetCustomAttribute<ViewAsAccessAttribute>()?.Access);
            });
    }

    /// <summary>Whether an <c>[Authorize]</c> declaration names a policy the module recognises.</summary>
    /// <param name="policy">The declared policy, or <c>null</c> for a bare declaration.</param>
    /// <param name="schemes">The declared authentication schemes, or <c>null</c> for the default.</param>
    /// <returns>
    /// <c>true</c> for one of the four JWT policies, or for a policy that is a member of
    /// <see cref="ApiKeyPolicyNames"/> declared with exactly the <c>ApiKey</c> scheme.
    /// </returns>
    private static bool IsKnownDeclaration(string? policy, string? schemes)
    {
        string[] jwtPolicies =
        [
            AuthorizationPolicies.AdminOnly,
            AuthorizationPolicies.AdminOrReseller,
            AuthorizationPolicies.AnyAuthenticated,
            AuthorizationPolicies.ResellerOnly,
        ];
        if (policy is null)
        {
            return false;
        }

        if (jwtPolicies.Contains(policy))
        {
            return true;
        }

        var apiKeyPolicies = typeof(ApiKeyPolicyNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field =>
            {
                return field.IsLiteral;
            })
            .Select(field =>
            {
                return (string?)field.GetRawConstantValue();
            });

        return apiKeyPolicies.Contains(policy)
            && string.Equals(schemes, ApiKeyDefaults.Scheme, StringComparison.Ordinal);
    }

    /// <summary>The <c>[Authorize]</c> declarations on an action and on its class.</summary>
    /// <param name="method">The action.</param>
    /// <returns>The declarations, the action's first.</returns>
    private static AuthorizeAttribute[] AuthorizeAttributesOf(MethodInfo method)
    {
        return method.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Concat(method.DeclaringType!.GetCustomAttributes<AuthorizeAttribute>(inherit: true))
            .ToArray();
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

    /// <summary>The policies an action's own attributes name, leaving the class's out.</summary>
    /// <param name="method">The action.</param>
    /// <returns>The policy names the action itself declares.</returns>
    private static string[] ActionPoliciesOf(MethodInfo method)
    {
        return method.GetCustomAttributes<AuthorizeAttribute>(inherit: false)
            .Select(attribute =>
            {
                return attribute.Policy ?? string.Empty;
            })
            .ToArray();
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
