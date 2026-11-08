using System.Reflection;
using Maran.Modules.Accounts.Controllers.External;
using Maran.Sdk.Contracts;
using Maran.Sdk.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;

namespace Maran.Modules.Accounts.Tests.Controllers;

/// <summary>
/// Pins how the provisioning controllers a reseller's API key reaches declare themselves (issue #7): one
/// key policy per controller at class level, the provisioning rate-limit bucket on the writes, no
/// view-as, and the idempotency header REQUIRED on every action that changes anything.
/// </summary>
public sealed class ResellerKeyProvisioningControllersTests
{
    /// <summary>The write controller names the write policy, alone, on the API-key scheme only.</summary>
    [Fact]
    public void The_write_controller_names_the_write_policy_on_the_key_scheme_alone()
    {
        var authorize = AuthorizeOf(typeof(ResellerKeyAccountWritesController));

        Assert.Equal(ApiKeyPolicyNames.AccountsWrite, authorize.Policy);
        Assert.Equal(ApiKeyDefaults.Scheme, authorize.AuthenticationSchemes);
    }

    /// <summary>The termination controller names the delete policy, alone, so a write-only key cannot destroy data.</summary>
    [Fact]
    public void The_termination_controller_names_the_delete_policy_on_the_key_scheme_alone()
    {
        var authorize = AuthorizeOf(typeof(ResellerKeyAccountTerminationController));

        Assert.Equal(ApiKeyPolicyNames.AccountsDelete, authorize.Policy);
        Assert.Equal(ApiKeyDefaults.Scheme, authorize.AuthenticationSchemes);
    }

    /// <summary>The detail controller names the read policy, alone, on the API-key scheme only.</summary>
    [Fact]
    public void The_detail_controller_names_the_read_policy_on_the_key_scheme_alone()
    {
        var authorize = AuthorizeOf(typeof(ResellerKeyAccountDetailsController));

        Assert.Equal(ApiKeyPolicyNames.AccountsRead, authorize.Policy);
        Assert.Equal(ApiKeyDefaults.Scheme, authorize.AuthenticationSchemes);
    }

    /// <summary>Only the controllers that change something use the provisioning bucket; the reads use the general one.</summary>
    [Fact]
    public void Writes_use_the_provisioning_bucket_and_reads_use_the_general_one()
    {
        Assert.Equal(RateLimitPolicies.Provisioning, RateLimitOf(typeof(ResellerKeyAccountWritesController)));
        Assert.Equal(RateLimitPolicies.Provisioning, RateLimitOf(typeof(ResellerKeyAccountTerminationController)));
        Assert.Equal(RateLimitPolicies.Api, RateLimitOf(typeof(ResellerKeyAccountDetailsController)));
    }

    /// <summary>Every provisioning controller refuses view-as.</summary>
    /// <param name="controller">The controller type.</param>
    [Theory]
    [InlineData(typeof(ResellerKeyAccountWritesController))]
    [InlineData(typeof(ResellerKeyAccountTerminationController))]
    [InlineData(typeof(ResellerKeyAccountDetailsController))]
    public void A_provisioning_controller_refuses_view_as(Type controller)
    {
        var declared = controller.GetCustomAttribute<ViewAsAccessAttribute>();

        Assert.NotNull(declared);
        Assert.Equal(ViewAsAccess.Refused, declared.Access);
    }

    /// <summary>Every action that changes anything is idempotent AND requires the header; the reads are neither.</summary>
    /// <param name="controller">The controller type.</param>
    [Theory]
    [InlineData(typeof(ResellerKeyAccountWritesController))]
    [InlineData(typeof(ResellerKeyAccountTerminationController))]
    [InlineData(typeof(ResellerKeyAccountDetailsController))]
    public void Every_changing_action_requires_the_idempotency_key_and_no_read_does(Type controller)
    {
        var actions = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method =>
            {
                return method.GetCustomAttributes().OfType<HttpMethodAttribute>().Any();
            })
            .ToList();
        Assert.NotEmpty(actions);

        foreach (var action in actions)
        {
            var reads = action.GetCustomAttributes().OfType<HttpGetAttribute>().Any();
            var idempotent = action.GetCustomAttribute<IdempotentAttribute>();

            if (reads)
            {
                Assert.Null(idempotent);
            }
            else
            {
                Assert.True(idempotent is { Required: true }, action.Name + " must require the Idempotency-Key header");
            }
        }
    }

    /// <summary>The termination action takes the account id alone: nothing a caller sends can skip the final backup.</summary>
    [Fact]
    public void The_termination_action_binds_only_the_route_id_and_the_token()
    {
        var action = typeof(ResellerKeyAccountTerminationController).GetMethod("TerminateAsync")!;

        var parameters = action.GetParameters().Select(parameter =>
        {
            return parameter.ParameterType;
        });

        Assert.Equal([typeof(Guid), typeof(CancellationToken)], parameters);
    }

    /// <summary>The authorization a controller declares on its class.</summary>
    /// <param name="controller">The controller type.</param>
    /// <returns>The single authorize attribute.</returns>
    private static AuthorizeAttribute AuthorizeOf(Type controller)
    {
        return Assert.Single(controller.GetCustomAttributes<AuthorizeAttribute>(inherit: false));
    }

    /// <summary>The rate-limit policy a controller names on its class.</summary>
    /// <param name="controller">The controller type.</param>
    /// <returns>The policy name.</returns>
    private static string RateLimitOf(Type controller)
    {
        return Assert.Single(controller.GetCustomAttributes<EnableRateLimitingAttribute>(inherit: false)).PolicyName!;
    }
}
