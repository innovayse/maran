using Maran.Modules.Sites.Controllers;
using Maran.Sdk.Contracts;
using Maran.Sdk.Filters;
using Microsoft.AspNetCore.Authorization;

namespace Maran.Modules.Sites.Tests.Controllers;

/// <summary>Pins how <see cref="SiteOptionsController"/> declares who may call it and what a view-as session may do.</summary>
public sealed class SiteOptionsControllerPolicyTests
{
    /// <summary>The class names the customer-level policy, alone.</summary>
    [Fact]
    public void The_options_controller_names_the_any_authenticated_policy()
    {
        var policies = typeof(SiteOptionsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Select(attribute =>
            {
                return attribute.Policy;
            })
            .ToArray();

        Assert.Equal(new[] { AuthorizationPolicies.AnyAuthenticated }, policies);
    }

    /// <summary>Reading is a view-as read and saving is refused to a view-as session.</summary>
    [Fact]
    public void Reading_is_a_view_as_read_and_saving_is_refused_to_a_view_as_session()
    {
        var read = typeof(SiteOptionsController).GetMethod(nameof(SiteOptionsController.GetAsync))!
            .GetCustomAttributes(typeof(ViewAsAccessAttribute), inherit: true)
            .Cast<ViewAsAccessAttribute>()
            .Single();
        var write = typeof(SiteOptionsController).GetMethod(nameof(SiteOptionsController.ReplaceAsync))!
            .GetCustomAttributes(typeof(ViewAsAccessAttribute), inherit: true)
            .Cast<ViewAsAccessAttribute>()
            .Single();

        Assert.Equal(ViewAsAccess.Read, read.Access);
        Assert.Equal(ViewAsAccess.Refused, write.Access);
    }

    /// <summary>Saving is idempotent under a retried key.</summary>
    [Fact]
    public void Saving_is_idempotent_under_a_retried_key()
    {
        var method = typeof(SiteOptionsController).GetMethod(nameof(SiteOptionsController.ReplaceAsync))!;

        Assert.NotEmpty(method.GetCustomAttributes(typeof(IdempotentAttribute), inherit: true));
    }
}
