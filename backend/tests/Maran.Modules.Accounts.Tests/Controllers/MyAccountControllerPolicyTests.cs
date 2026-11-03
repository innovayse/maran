using Maran.Modules.Accounts.Controllers;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace Maran.Modules.Accounts.Tests.Controllers;

/// <summary>
/// Pins how the Accounts controllers declare who may call them, now that the panel's fallback
/// policy is administrator-only: the customer-facing controller must name the customer-level
/// policy itself, because naming nothing would send a customer to the fallback and refuse them.
/// </summary>
public sealed class MyAccountControllerPolicyTests
{
    /// <summary>The customer's own account read names the customer-level policy, alone.</summary>
    [Fact]
    public void The_my_account_controller_names_the_any_authenticated_policy()
    {
        var policies = PoliciesOf(typeof(MyAccountController));

        Assert.Equal(new[] { AuthorizationPolicies.AnyAuthenticated }, policies);
    }

    /// <summary>
    /// The account-state names controller names the customer-level policy, alone: a customer's own
    /// account page prints its state with the same names the administrator's filter offers.
    /// </summary>
    [Fact]
    public void The_account_statuses_controller_names_the_any_authenticated_policy()
    {
        var policies = PoliciesOf(typeof(AccountStatusesController));

        Assert.Equal(new[] { AuthorizationPolicies.AnyAuthenticated }, policies);
    }

    /// <summary>The account and plan controllers admit an administrator or a reseller, and name nothing else.</summary>
    /// <param name="controller">The controller type.</param>
    [Theory]
    [InlineData(typeof(AccountsController))]
    [InlineData(typeof(PlansController))]
    public void A_managed_controller_names_the_administrator_or_reseller_policy(Type controller)
    {
        Assert.Equal(new[] { AuthorizationPolicies.AdminOrReseller }, PoliciesOf(controller));
    }

    /// <summary>The controllers only an administrator may drive keep the administrator policy.</summary>
    /// <param name="controller">The controller type.</param>
    [Theory]
    [InlineData(typeof(AccountHomeGroupsController))]
    [InlineData(typeof(ResellersController))]
    public void An_administrator_controller_names_the_administrator_policy(Type controller)
    {
        Assert.Equal(new[] { AuthorizationPolicies.AdminOnly }, PoliciesOf(controller));
    }

    /// <summary>The reseller's own profile controller names the reseller policy, alone.</summary>
    [Fact]
    public void The_reseller_controller_names_the_reseller_only_policy()
    {
        Assert.Equal(new[] { AuthorizationPolicies.ResellerOnly }, PoliciesOf(typeof(ResellerController)));
    }

    /// <summary>No Accounts controller grants anonymity on the class.</summary>
    [Fact]
    public void No_accounts_controller_grants_anonymity()
    {
        var offenders = typeof(MyAccountController).Assembly
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
