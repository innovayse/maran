using Maran.Modules.Sites.Controllers;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace Maran.Modules.Sites.Tests.Controllers;

/// <summary>
/// Pins how the Sites controller declares who may call it, now that the panel's fallback policy is
/// administrator-only: the customer's sites are reachable only because the class names the
/// customer-level policy, and the one server-wide action narrows it to the administrator.
/// </summary>
public sealed class SitesControllerPolicyTests
{
    /// <summary>The class names the customer-level policy, alone.</summary>
    [Fact]
    public void The_sites_controller_names_the_any_authenticated_policy()
    {
        var policies = typeof(SitesController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Select(attribute =>
            {
                return attribute.Policy;
            })
            .ToArray();

        Assert.Equal(new[] { AuthorizationPolicies.AnyAuthenticated }, policies);
    }

    /// <summary>Switching a PHP extension changes every account's pool of a version and stays administrator-only.</summary>
    [Fact]
    public void Switching_a_php_extension_names_the_administrator_policy_and_refuses_view_as()
    {
        var method = typeof(SitesController).GetMethod(nameof(SitesController.SetPhpExtensionAsync));

        Assert.NotNull(method);
        var policies = method!
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Select(attribute =>
            {
                return attribute.Policy;
            })
            .ToArray();
        Assert.Equal(new[] { AuthorizationPolicies.AdminOnly }, policies);
        Assert.Equal(ViewAsAccess.Refused, method.GetCustomAttributes(typeof(ViewAsAccessAttribute), inherit: true)
            .Cast<ViewAsAccessAttribute>().Single().Access);
        Assert.Single(method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpPutAttribute), inherit: true));
    }

    /// <summary>The extension list is open to anyone who may see the versions, and declares its view-as access.</summary>
    [Fact]
    public void Listing_php_extensions_adds_no_administrator_policy_and_declares_view_as_read()
    {
        var method = typeof(SitesController).GetMethod(nameof(SitesController.GetPhpExtensionsAsync));

        Assert.NotNull(method);
        Assert.DoesNotContain(
            method!.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Cast<AuthorizeAttribute>(),
            attribute =>
            {
                return attribute.Policy == AuthorizationPolicies.AdminOnly;
            });
        Assert.Equal(ViewAsAccess.Read, method.GetCustomAttributes(typeof(ViewAsAccessAttribute), inherit: true)
            .Cast<ViewAsAccessAttribute>().Single().Access);
    }

    /// <summary>No Sites controller grants anonymity on the class.</summary>
    [Fact]
    public void No_sites_controller_grants_anonymity()
    {
        var offenders = typeof(SitesController).Assembly
            .GetTypes()
            .Where(type =>
            {
                return typeof(Microsoft.AspNetCore.Mvc.ControllerBase).IsAssignableFrom(type);
            })
            .Where(type =>
            {
                return type.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true).Length > 0;
            })
            .Select(type =>
            {
                return type.Name;
            })
            .ToArray();

        Assert.Empty(offenders);
    }
}
