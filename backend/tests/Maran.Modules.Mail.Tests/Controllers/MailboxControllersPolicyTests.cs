using System.Reflection;
using Maran.Modules.Mail.Controllers;
using Maran.Sdk.Contracts;
using Maran.Sdk.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Maran.Modules.Mail.Tests.Controllers;

/// <summary>
/// What the mailbox and autoresponder controllers declare: a policy on the class, no anonymous door, the
/// Mail account feature, the API rate limit, and a view-as access on every action that decides what an
/// operator viewing as a customer may do.
/// </summary>
public sealed class MailboxControllersPolicyTests
{
    /// <summary>The controllers this group owns; other groups' controllers have their own tests.</summary>
    private static readonly Type[] Controllers = [typeof(MailboxesController), typeof(MailboxAutoresponderController)];

    /// <summary>Each controller names exactly the any-authenticated policy, alone, on its class.</summary>
    /// <param name="controller">The controller.</param>
    [Theory]
    [InlineData(typeof(MailboxesController))]
    [InlineData(typeof(MailboxAutoresponderController))]
    public void A_controller_names_its_policy_on_the_class(Type controller)
    {
        var policies = controller
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Select(attribute =>
            {
                return attribute.Policy ?? string.Empty;
            })
            .ToArray();

        Assert.Equal(new[] { AuthorizationPolicies.AnyAuthenticated }, policies);
    }

    /// <summary>Each controller requires the Mail account feature.</summary>
    /// <param name="controller">The controller.</param>
    [Theory]
    [InlineData(typeof(MailboxesController))]
    [InlineData(typeof(MailboxAutoresponderController))]
    public void A_controller_requires_the_mail_account_feature(Type controller)
    {
        var features = controller
            .GetCustomAttributes(typeof(RequiresAccountFeatureAttribute), inherit: true)
            .Cast<RequiresAccountFeatureAttribute>()
            .Select(attribute =>
            {
                return attribute.Feature;
            })
            .ToArray();

        Assert.Equal(new[] { AccountFeature.Mail }, features);
    }

    /// <summary>Each controller is held to the API rate limit.</summary>
    /// <param name="controller">The controller.</param>
    [Theory]
    [InlineData(typeof(MailboxesController))]
    [InlineData(typeof(MailboxAutoresponderController))]
    public void A_controller_is_held_to_the_api_rate_limit(Type controller)
    {
        var limits = controller
            .GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: true)
            .Cast<EnableRateLimitingAttribute>()
            .Select(attribute =>
            {
                return attribute.PolicyName;
            })
            .ToArray();

        Assert.Equal(new[] { RateLimitPolicies.Api }, limits);
    }

    /// <summary>The routes are the ones the single-page app calls.</summary>
    [Fact]
    public void The_routes_are_the_ones_the_app_calls()
    {
        Assert.Equal("api/v1/mailboxes", RouteOf(typeof(MailboxesController)));
        Assert.Equal("api/v1/mailboxes/{mailboxId:guid}/autoresponder", RouteOf(typeof(MailboxAutoresponderController)));
    }

    /// <summary>No member of these controllers grants anonymity.</summary>
    [Fact]
    public void No_mailbox_controller_grants_anonymity()
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

    /// <summary>No action overrides the class policy.</summary>
    [Fact]
    public void No_mailbox_action_overrides_the_class_policy()
    {
        var offenders = Actions()
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

    /// <summary>Every action declares its view-as access: a read is Read, everything else is Refused.</summary>
    [Fact]
    public void Every_action_declares_a_view_as_access_that_matches_its_verb()
    {
        var actions = Actions().ToArray();
        Assert.NotEmpty(actions);

        foreach (var action in actions)
        {
            var declared = action.GetCustomAttribute<ViewAsAccessAttribute>(inherit: true)
                ?? action.DeclaringType!.GetCustomAttribute<ViewAsAccessAttribute>(inherit: true);
            Assert.True(
                declared is not null, action.DeclaringType!.Name + "." + action.Name + " declares no view-as access");

            var isRead = action.GetCustomAttributes<HttpGetAttribute>().Any();
            Assert.Equal(isRead ? ViewAsAccess.Read : ViewAsAccess.Refused, declared!.Access);
        }
    }

    /// <summary>The walk sees every action of both controllers, so the checks above are not vacuous.</summary>
    [Fact]
    public void The_walk_sees_every_action_of_the_two_controllers()
    {
        var names = Actions()
            .Select(method =>
            {
                return method.DeclaringType!.Name + "." + method.Name;
            })
            .Order(StringComparer.Ordinal)
            .ToArray();

        var expected = new[]
        {
            "MailboxAutoresponderController.ClearAsync",
            "MailboxAutoresponderController.GetAsync",
            "MailboxAutoresponderController.SetAsync",
            "MailboxesController.ChangePasswordAsync",
            "MailboxesController.CreateAsync",
            "MailboxesController.DeleteAsync",
            "MailboxesController.GetAllAsync",
            "MailboxesController.UpdateAsync",
        };
        Assert.Equal(expected, names);
    }

    /// <summary>The route template a controller class declares.</summary>
    /// <param name="controller">The controller.</param>
    /// <returns>The template of its one <c>[Route]</c> attribute.</returns>
    private static string RouteOf(Type controller)
    {
        return controller.GetCustomAttributes<RouteAttribute>(inherit: false).Single().Template;
    }

    /// <summary>The public instance methods the controllers declare themselves: the actions.</summary>
    /// <returns>The actions.</returns>
    private static IEnumerable<MethodInfo> Actions()
    {
        return Controllers.SelectMany(type =>
        {
            return type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        });
    }

    /// <summary>The controller types and their actions, for the checks that apply to both.</summary>
    /// <returns>The members.</returns>
    private static IEnumerable<MemberInfo> MembersOfTheControllers()
    {
        return Controllers.SelectMany(type =>
        {
            return new MemberInfo[] { type }.Concat(Actions().Where(method =>
            {
                return method.DeclaringType == type;
            }));
        });
    }
}
