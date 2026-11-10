using System.Reflection;
using Maran.Modules.Mail.Controllers;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Maran.Modules.Mail.Tests.Controllers;

/// <summary>
/// Pins how the two server administration controllers declare who may call them: administrators
/// only, on the class, with no action loosening it.
/// </summary>
public sealed class MailServerControllerPolicyTests
{
    /// <summary>The controllers under test.</summary>
    private static readonly Type[] Controllers = [typeof(MailServerController), typeof(MailQueueController)];

    /// <summary>Each controller names the administrator-only policy, alone, on its class.</summary>
    /// <param name="controller">The controller type.</param>
    [Theory]
    [InlineData(typeof(MailServerController))]
    [InlineData(typeof(MailQueueController))]
    public void A_server_administration_controller_names_the_administrator_policy_on_its_class(Type controller)
    {
        var policies = controller
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Select(attribute =>
            {
                return attribute.Policy ?? string.Empty;
            })
            .ToArray();

        Assert.Equal(new[] { AuthorizationPolicies.AdminOnly }, policies);
    }

    /// <summary>Every action of both controllers sits under that class policy: none grants anonymity or carries a policy of its own.</summary>
    [Fact]
    public void No_action_grants_anonymity_or_overrides_the_class_policy()
    {
        var offenders = Actions()
            .Where(method =>
            {
                return method.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true).Length > 0
                    || method.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Length > 0;
            })
            .Select(method =>
            {
                return method.DeclaringType!.Name + "." + method.Name;
            })
            .ToArray();

        Assert.Empty(offenders);
    }

    /// <summary>The walk sees every action the two controllers expose, so the checks above cannot pass on nothing.</summary>
    [Fact]
    public void The_walk_sees_every_action_of_both_controllers()
    {
        var names = Actions()
            .Select(method =>
            {
                return method.DeclaringType!.Name + "." + method.Name;
            })
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["MailQueueController.DeleteAsync",
                "MailQueueController.FlushAsync",
                "MailQueueController.GetAllAsync",
                "MailQueueController.GetCountAsync",
                "MailServerController.DisableAsync",
                "MailServerController.EnableAsync",
                "MailServerController.GetAsync",
                "MailServerController.UpdateSettingsAsync",],
            names);
    }

    /// <summary>Each controller sits under the route the wire contract names.</summary>
    /// <param name="controller">The controller type.</param>
    /// <param name="route">The route it must carry.</param>
    [Theory]
    [InlineData(typeof(MailServerController), "api/v1/mail-server")]
    [InlineData(typeof(MailQueueController), "api/v1/mail-queue")]
    public void A_controller_carries_the_route_the_wire_contract_names(Type controller, string route)
    {
        var routes = controller
            .GetCustomAttributes<RouteAttribute>(inherit: false)
            .Select(attribute =>
            {
                return attribute.Template;
            })
            .ToArray();

        Assert.Equal(new[] { route }, routes);
    }

    /// <summary>The public actions the two controllers declare.</summary>
    /// <returns>Every public instance method declared on them.</returns>
    private static IEnumerable<MethodInfo> Actions()
    {
        return Controllers.SelectMany(controller =>
        {
            return controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        });
    }
}
