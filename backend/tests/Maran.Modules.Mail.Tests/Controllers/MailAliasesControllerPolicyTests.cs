using System.Reflection;
using Maran.Modules.Mail.Controllers;
using Maran.Sdk.Contracts;
using Maran.Sdk.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Maran.Modules.Mail.Tests.Controllers;

/// <summary>
/// Pins how the aliases controller declares who may call it, which feature it needs, how fast it may be
/// called, and what an operator viewing the panel as a customer may do with it.
/// </summary>
public sealed class MailAliasesControllerPolicyTests
{
    /// <summary>The controller names exactly one policy, alone, on its class.</summary>
    [Fact]
    public void The_controller_names_the_any_authenticated_policy_on_the_class()
    {
        var policies = typeof(MailAliasesController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Select(attribute =>
            {
                return attribute.Policy ?? string.Empty;
            })
            .ToArray();

        Assert.Equal([AuthorizationPolicies.AnyAuthenticated], policies);
    }

    /// <summary>Neither the class nor any action grants anonymity or overrides the class policy.</summary>
    [Fact]
    public void No_member_grants_anonymity_or_overrides_the_class_policy()
    {
        var offenders = Members()
            .Where(member =>
            {
                return member.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true).Length > 0
                    || (member is MethodInfo
                        && member.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Length > 0);
            })
            .Select(member =>
            {
                return member.Name;
            })
            .ToArray();

        Assert.Empty(offenders);
    }

    /// <summary>The controller lives under the full versioned path the single-page app calls.</summary>
    [Fact]
    public void The_route_is_the_versioned_mail_aliases_path()
    {
        var route = typeof(MailAliasesController).GetCustomAttribute<RouteAttribute>(inherit: false);

        Assert.NotNull(route);
        Assert.Equal("api/v1/mail-aliases", route.Template);
    }

    /// <summary>A plan without mail is refused on every request, whatever the menu shows.</summary>
    [Fact]
    public void The_controller_requires_the_mail_account_feature()
    {
        var features = typeof(MailAliasesController)
            .GetCustomAttributes<RequiresAccountFeatureAttribute>()
            .Select(attribute =>
            {
                return attribute.Feature;
            })
            .ToArray();

        Assert.Equal([AccountFeature.Mail], features);
    }

    /// <summary>The controller is held to the panel's general rate limit.</summary>
    [Fact]
    public void The_controller_is_rate_limited_by_the_api_policy()
    {
        var limit = typeof(MailAliasesController).GetCustomAttribute<EnableRateLimitingAttribute>();

        Assert.NotNull(limit);
        Assert.Equal(RateLimitPolicies.Api, limit.PolicyName);
    }

    /// <summary>The list may be read while viewing as a customer; creating, changing and deleting are refused.</summary>
    /// <param name="action">The action's name.</param>
    /// <param name="expected">The view-as access it must declare.</param>
    [Theory]
    [InlineData("GetAllAsync", ViewAsAccess.Read)]
    [InlineData("CreateAsync", ViewAsAccess.Refused)]
    [InlineData("UpdateAsync", ViewAsAccess.Refused)]
    [InlineData("DeleteAsync", ViewAsAccess.Refused)]
    public void Each_action_declares_its_view_as_access(string action, ViewAsAccess expected)
    {
        var method = typeof(MailAliasesController).GetMethod(action);

        Assert.NotNull(method);
        var declared = method.GetCustomAttribute<ViewAsAccessAttribute>();
        Assert.NotNull(declared);
        Assert.Equal(expected, declared.Access);
    }

    /// <summary>The walk sees all four actions, so the per-action check above cannot pass on nothing.</summary>
    [Fact]
    public void The_controller_declares_exactly_the_four_actions()
    {
        var actions = typeof(MailAliasesController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method =>
            {
                return method.Name;
            })
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["CreateAsync", "DeleteAsync", "GetAllAsync", "UpdateAsync"], actions);
    }

    /// <summary>Every public action declares its view-as access itself.</summary>
    [Fact]
    public void Every_public_action_declares_a_view_as_access()
    {
        var undeclared = typeof(MailAliasesController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method =>
            {
                return method.GetCustomAttribute<ViewAsAccessAttribute>() is null;
            })
            .Select(method =>
            {
                return method.Name;
            })
            .ToArray();

        Assert.Empty(undeclared);
    }

    /// <summary>The controller class and every method it declares.</summary>
    /// <returns>The members whose attributes decide who may call the controller.</returns>
    private static IEnumerable<MemberInfo> Members()
    {
        return new MemberInfo[] { typeof(MailAliasesController) }.Concat(
            typeof(MailAliasesController).GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
    }
}
