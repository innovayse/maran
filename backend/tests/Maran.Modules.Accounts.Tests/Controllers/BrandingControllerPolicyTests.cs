using Maran.Modules.Accounts.Controllers;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace Maran.Modules.Accounts.Tests.Controllers;

/// <summary>
/// Pins who may call the two branding controllers: the reseller sets its branding under the
/// reseller-only policy, and every signed-in caller reads what its shell shows under the customer-level one.
/// </summary>
public sealed class BrandingControllerPolicyTests
{
    /// <summary>The reseller's own branding controller names the reseller policy, alone.</summary>
    [Fact]
    public void The_reseller_branding_controller_names_the_reseller_only_policy()
    {
        Assert.Equal(new[] { AuthorizationPolicies.ResellerOnly }, PoliciesOf(typeof(ResellerBrandingController)));
    }

    /// <summary>The shell's branding read names the customer-level policy, alone.</summary>
    [Fact]
    public void The_my_branding_controller_names_the_any_authenticated_policy()
    {
        Assert.Equal(new[] { AuthorizationPolicies.AnyAuthenticated }, PoliciesOf(typeof(MyBrandingController)));
    }

    /// <summary>The policy names a controller declares on its class.</summary>
    /// <param name="controller">The controller type.</param>
    /// <returns>The policy names, empty strings for a bare declaration.</returns>
    private static string[] PoliciesOf(Type controller)
    {
        return controller.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Select(attribute =>
            {
                return attribute.Policy ?? string.Empty;
            })
            .ToArray();
    }
}
