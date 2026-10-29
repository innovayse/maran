using System.Reflection;
using Maran.Modules.Licensing.Controllers;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Maran.Modules.Licensing.Tests.Controllers;

/// <summary>
/// Pins how the Licensing controllers declare who may call them now that the panel's fallback policy is
/// administrator-only: a controller that names nothing is refused for a customer, so each one
/// names its policy on the class, and no action quietly overrides it.
/// </summary>
public sealed class LicensingControllerPolicyTests
{
    /// <summary>Each controller names exactly the policy it is meant for, alone, on its class.</summary>
    /// <param name="controller">The controller type.</param>
    /// <param name="policy">The policy the controller must name.</param>
    [Theory]
    [InlineData(typeof(LicensingController), AuthorizationPolicies.AdminOnly)]
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

    /// <summary>No Licensing controller grants anonymity, on the class or on any action.</summary>
    [Fact]
    public void No_licensing_controller_grants_anonymity()
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

    /// <summary>No action of a Licensing controller carries an authorization attribute of its own.</summary>
    [Fact]
    public void No_licensing_action_overrides_the_class_policy()
    {
        var offenders = MembersOfTheControllers()
            .OfType<MethodInfo>()
            .Where(method =>
            {
                return method.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Length > 0;
            })
            .Select(method =>
            {
                return method.DeclaringType!.Name + "." + method.Name;
            })
            .ToArray();

        Assert.Empty(offenders);
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
            nameof(LicensingController),
        };
        Assert.Equal(expected.Order(StringComparer.Ordinal), names);
    }

    /// <summary>The controllers the module ships.</summary>
    /// <returns>Every concrete controller type in the module's assembly.</returns>
    private static IEnumerable<Type> ControllersOfThisModule()
    {
        return typeof(LicensingController).Assembly
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
