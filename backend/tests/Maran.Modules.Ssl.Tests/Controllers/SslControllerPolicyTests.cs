using System.Reflection;
using Maran.Modules.Ssl.Controllers;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Maran.Modules.Ssl.Tests.Controllers;

/// <summary>
/// Pins how the Ssl controllers declare who may call them now that the panel's fallback policy is
/// administrator-only: a controller that names nothing is refused for a customer, so each one
/// names its policy on the class, and no action quietly overrides it.
/// </summary>
public sealed class SslControllerPolicyTests
{
    /// <summary>Each controller names exactly the policy it is meant for, alone, on its class.</summary>
    /// <param name="controller">The controller type.</param>
    /// <param name="policy">The policy the controller must name.</param>
    [Theory]
    [InlineData(typeof(CertificatesController), AuthorizationPolicies.AnyAuthenticated)]
    [InlineData(typeof(PanelCertificateController), AuthorizationPolicies.AdminOnly)]
    public void A_controller_names_its_policy_on_the_class(Type controller, string policy)
    {
        var policies = controller
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Select(attribute =>
            {
                return attribute.Policy ?? string.Empty;
            })
            .ToArray();

        Assert.Equal(new[] { policy }, policies);
    }

    /// <summary>No Ssl controller grants anonymity, on the class or on any action.</summary>
    [Fact]
    public void No_ssl_controller_grants_anonymity()
    {
        var offenders = MembersOfTheControllers()
            .Where(member =>
            {
                return member.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true).Length > 0;
            })
            .Select(member =>
            {
                return member.DeclaringType!.Name + "." + member.Name;
            })
            .ToArray();

        Assert.Empty(offenders);
    }

    /// <summary>
    /// No action of a Ssl controller carries an authorization attribute of its own, except the one
    /// administrator-only action added to the customer-facing controller, which names AdminOnly.
    /// </summary>
    [Fact]
    public void No_ssl_action_overrides_the_class_policy_except_the_named_administrator_only_ones()
    {
        var offenders = MembersOfTheControllers()
            .OfType<MethodInfo>()
            .Where(method =>
            {
                return method.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Length > 0;
            })
            .Where(method =>
            {
                return !IsDeclaredAdministratorOnlyAction(method);
            })
            .Select(method =>
            {
                return method.DeclaringType!.Name + "." + method.Name;
            })
            .ToArray();

        Assert.Empty(offenders);
    }

    /// <summary>The ACME directory read on the customer-facing controller is administrator only, and alone in carrying a policy.</summary>
    [Fact]
    public void The_acme_directory_action_names_the_administrator_only_policy()
    {
        var method = typeof(CertificatesController).GetMethod(nameof(CertificatesController.GetAcmeDirectoryAsync))!;

        var policies = method
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Select(attribute =>
            {
                return attribute.Policy ?? string.Empty;
            })
            .ToArray();

        Assert.Equal(new[] { AuthorizationPolicies.AdminOnly }, policies);
    }

    /// <summary>
    /// The self-signed restore is an HTTP DELETE on the administrator-only controller and carries no
    /// authorization attribute of its own that could widen the class policy (issue #425).
    /// </summary>
    [Fact]
    public void The_self_signed_restore_action_is_a_delete_that_inherits_the_administrator_only_class_policy()
    {
        var method = typeof(PanelCertificateController).GetMethod(nameof(PanelCertificateController.RestoreSelfSignedAsync))!;

        Assert.Single(method.GetCustomAttributes(typeof(HttpDeleteAttribute), inherit: true));
        Assert.Empty(method.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true));
        Assert.Empty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));
        var classPolicy = typeof(PanelCertificateController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Single();
        Assert.Equal(AuthorizationPolicies.AdminOnly, classPolicy.Policy);
    }

    /// <summary>The walk sees every controller of the module, so the checks above cannot pass on nothing.</summary>
    [Fact]
    public void The_walk_sees_the_controllers_of_the_module()
    {
        var names = ControllersOfThisModule()
            .Select(type =>
            {
                return type.Name;
            })
            .Order(StringComparer.Ordinal)
            .ToArray();

        var expected = new[]
        {
            nameof(CertificatesController),
            nameof(PanelCertificateController),
        };
        Assert.Equal(expected.Order(StringComparer.Ordinal), names);
    }

    /// <summary>Whether a method is one of the administrator-only actions allowed on a customer-facing controller.</summary>
    /// <param name="method">The action to judge.</param>
    /// <returns>True for <see cref="CertificatesController.GetAcmeDirectoryAsync"/> naming AdminOnly alone.</returns>
    private static bool IsDeclaredAdministratorOnlyAction(MethodInfo method)
    {
        if (method.DeclaringType != typeof(CertificatesController)
            || method.Name != nameof(CertificatesController.GetAcmeDirectoryAsync))
        {
            return false;
        }

        return method
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .All(attribute =>
            {
                return attribute.Policy == AuthorizationPolicies.AdminOnly;
            });
    }

    /// <summary>The controllers the module ships.</summary>
    /// <returns>Every concrete controller type in the module's assembly.</returns>
    private static IEnumerable<Type> ControllersOfThisModule()
    {
        return typeof(CertificatesController).Assembly
            .GetTypes()
            .Where(type =>
            {
                return !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type);
            });
    }

    /// <summary>Every controller class and every method it declares.</summary>
    /// <returns>The members whose attributes decide who may call the module.</returns>
    private static IEnumerable<MemberInfo> MembersOfTheControllers()
    {
        return ControllersOfThisModule()
            .SelectMany(type =>
            {
                return new MemberInfo[] { type }.Concat(
                    type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
            });
    }
}
