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

    /// <summary>Installing a PHP version is a server-wide act and stays administrator-only.</summary>
    [Fact]
    public void Installing_a_php_version_names_the_administrator_policy()
    {
        var method = typeof(SitesController).GetMethod(nameof(SitesController.InstallPhpVersionAsync));

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
