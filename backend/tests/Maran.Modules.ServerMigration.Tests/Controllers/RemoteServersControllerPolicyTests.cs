using System.Reflection;
using Maran.Modules.ServerMigration.Controllers;
using Maran.Sdk.Contracts;
using Maran.Sdk.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;

namespace Maran.Modules.ServerMigration.Tests.Controllers;

/// <summary>
/// Pins how the remote servers surface declares who may call it. The surface is administrator-only: a signed-in
/// customer or reseller is refused 403 on every route, because the policy on the class is the whole of the
/// authorisation and nothing below it loosens it. The walk is proven to see the actions before its silence is believed.
/// </summary>
public sealed class RemoteServersControllerPolicyTests
{
    /// <summary>The actions the controller is promised to have, so a walk that found none cannot pass.</summary>
    private static readonly string[] ExpectedActions =
    [
        nameof(RemoteServersController.ConfirmHostKeyAsync),
        nameof(RemoteServersController.CreateAsync),
        nameof(RemoteServersController.DeleteAsync),
        nameof(RemoteServersController.DiscoverAsync),
        nameof(RemoteServersController.GetAccountAsync),
        nameof(RemoteServersController.GetAccountsAsync),
        nameof(RemoteServersController.GetAllAsync),
        nameof(RemoteServersController.GetAsync),
        nameof(RemoteServersController.RescanHostKeysAsync),
        nameof(RemoteServersController.ReplaceAsync),
        nameof(RemoteServersController.TestAsync),
    ];

    /// <summary>The controller's action methods.</summary>
    /// <returns>Every public method that answers an HTTP verb.</returns>
    private static List<MethodInfo> Actions()
    {
        return typeof(RemoteServersController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method =>
            {
                return method.GetCustomAttributes<HttpMethodAttribute>().Any();
            })
            .ToList();
    }

    /// <summary>The controllers of the module, each with its own policy test: a walk that found fewer would be blind.</summary>
    private static readonly string[] ModuleControllers =
    [
        nameof(AccountExportsController),
        nameof(AccountImportsController),
        nameof(MigrationJobsController),
        nameof(MigrationPullController),
        nameof(RemoteServersController),
    ];

    /// <summary>The walk sees every action the controller is promised to have, and the module's controllers are the five it is promised.</summary>
    [Fact]
    public void The_walk_sees_the_actions_and_the_modules_controllers()
    {
        var names = Actions().Select(method =>
        {
            return method.Name;
        }).Order(StringComparer.Ordinal).ToArray();
        var controllers = typeof(RemoteServersController).Assembly
            .GetTypes()
            .Where(type =>
            {
                return !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type);
            })
            .Select(type =>
            {
                return type.Name;
            })
            .ToArray();

        Assert.Equal(ExpectedActions.Order(StringComparer.Ordinal), names);
        Assert.Equal(ModuleControllers.Order(StringComparer.Ordinal), controllers.Order(StringComparer.Ordinal));
    }

    /// <summary>The class names the administrator policy, alone, and no action names another or grants anonymity.</summary>
    [Fact]
    public void The_class_names_the_administrator_policy_and_no_action_loosens_it()
    {
        var policies = typeof(RemoteServersController)
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Select(attribute =>
            {
                return attribute.Policy ?? string.Empty;
            })
            .ToArray();

        Assert.Equal([AuthorizationPolicies.AdminOnly], policies);
        Assert.Empty(typeof(RemoteServersController).GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
        Assert.All(Actions(), method =>
        {
            Assert.Empty(method.GetCustomAttributes<AuthorizeAttribute>(inherit: true));
            Assert.Empty(method.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
        });
    }

    /// <summary>View-as is refused on the class and no action turns it back on.</summary>
    [Fact]
    public void View_as_is_refused_on_the_class_and_on_every_action()
    {
        var onClass = typeof(RemoteServersController).GetCustomAttribute<ViewAsAccessAttribute>();

        Assert.NotNull(onClass);
        Assert.Equal(ViewAsAccess.Refused, onClass!.Access);
        Assert.All(Actions(), method =>
        {
            var own = method.GetCustomAttribute<ViewAsAccessAttribute>();
            Assert.True(own is null || own.Access == ViewAsAccess.Refused, method.Name);
        });
    }

    /// <summary>The surface is rate limited by the panel's api policy and sits at the versioned path.</summary>
    [Fact]
    public void The_surface_is_rate_limited_and_sits_at_the_versioned_path()
    {
        var rateLimit = typeof(RemoteServersController).GetCustomAttribute<EnableRateLimitingAttribute>();
        var route = typeof(RemoteServersController).GetCustomAttribute<RouteAttribute>(inherit: false);

        Assert.NotNull(rateLimit);
        Assert.Equal(RateLimitPolicies.Api, rateLimit!.PolicyName);
        Assert.NotNull(route);
        Assert.Equal("api/v1/remote-servers", route!.Template);
    }

    /// <summary>Registration is idempotent under a client's key; no other action carries the filter.</summary>
    [Fact]
    public void Only_the_registration_is_idempotent()
    {
        var idempotent = Actions()
            .Where(method =>
            {
                return method.GetCustomAttributes<IdempotentAttribute>().Any();
            })
            .Select(method =>
            {
                return method.Name;
            })
            .ToArray();

        Assert.Equal([nameof(RemoteServersController.CreateAsync)], idempotent);
    }

    /// <summary>Every action declares the refusals an unauthenticated and a non-administrator caller get.</summary>
    [Fact]
    public void Every_action_declares_401_and_403()
    {
        Assert.All(Actions(), method =>
        {
            var statuses = method.GetCustomAttributes<ProducesResponseTypeAttribute>()
                .Select(attribute =>
                {
                    return attribute.StatusCode;
                })
                .ToHashSet();

            Assert.Contains(StatusCodes.Status401Unauthorized, statuses);
            Assert.Contains(StatusCodes.Status403Forbidden, statuses);
        });
    }

    /// <summary>The registration answers 201 and the removal answers a body naming the key to take out, not 204.</summary>
    [Fact]
    public void The_registration_answers_201_and_the_removal_answers_a_body()
    {
        var created = typeof(RemoteServersController).GetMethod(nameof(RemoteServersController.CreateAsync))!
            .GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute =>
            {
                return attribute.StatusCode;
            })
            .ToArray();
        var removed = typeof(RemoteServersController).GetMethod(nameof(RemoteServersController.DeleteAsync))!
            .GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute =>
            {
                return attribute.StatusCode;
            })
            .ToArray();

        Assert.Contains(StatusCodes.Status201Created, created);
        Assert.Contains(StatusCodes.Status200OK, removed);
        Assert.DoesNotContain(StatusCodes.Status204NoContent, removed);
    }
}
