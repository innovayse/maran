using System.Text.Json;
using Maran.Host.Modules;
using Maran.Host.Tests.Fixtures;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.Tests.Modules;

/// <summary>
/// Which modules <c>GET /api/v1/modules</c> offers to each case of caller: an administrator
/// everything, a reseller what is addressed to everyone or to administrators and resellers, anyone
/// else what is addressed to everyone.
/// </summary>
/// <remarks>
/// Presentation only: the endpoints behind a module refuse regardless of what this list contains.
/// Every expectation is derived from the registry's own manifests, and each case is paired with the
/// audiences it must NOT be offered, so a filter that offered everything to everyone cannot pass.
/// </remarks>
public sealed class ModulesEndpointAudienceTests : IClassFixture<PanelTestFactory>
{
    /// <summary>The booted host factory the per-caller hosts derive from.</summary>
    private readonly PanelTestFactory _factory;

    /// <summary>Captures the shared in-memory host factory.</summary>
    /// <param name="factory">The hermetic host factory.</param>
    public ModulesEndpointAudienceTests(PanelTestFactory factory)
    {
        _factory = factory;
    }

    /// <summary>An administrator is offered every module whatever its audience.</summary>
    /// <returns>Completes once the assertion has run.</returns>
    [Fact]
    public async Task An_administrator_is_offered_every_module_whatever_its_audience()
    {
        var offered = await OfferedToAsync(new AdministratorCaller(Guid.NewGuid(), "root"));

        Assert.Equal(ModuleRegistry.All.Select(module => { return module.Manifest.Id; }).ToList(), offered);
    }

    /// <summary>A reseller is offered what is addressed to everyone or to administrators and resellers, and no more.</summary>
    /// <returns>Completes once the assertion has run.</returns>
    [Fact]
    public async Task A_reseller_is_offered_the_everyone_and_the_admin_or_reseller_modules_and_no_more()
    {
        var offered = await OfferedToAsync(new ResellerCaller(Guid.NewGuid(), "reseller", Guid.NewGuid()));

        var expected = IdsAddressedTo(ModuleAudience.Everyone, ModuleAudience.AdminOrReseller);
        Assert.Equal(expected, offered);

        // The controls: the reseller side of the line is not empty and the administrator side is
        // not vacuous, or the equality above would hold for a filter that had stopped looking.
        Assert.NotEmpty(IdsAddressedTo(ModuleAudience.AdminOrReseller));
        Assert.NotEmpty(IdsAddressedTo(ModuleAudience.AdministratorOnly));
        Assert.Empty(offered.Intersect(IdsAddressedTo(ModuleAudience.AdministratorOnly)));
    }

    /// <summary>A customer is offered only what is addressed to everyone.</summary>
    /// <returns>Completes once the assertion has run.</returns>
    [Fact]
    public async Task A_customer_is_offered_only_the_modules_addressed_to_everyone()
    {
        var offered = await OfferedToAsync(new CustomerCaller(Guid.NewGuid(), "acme", Guid.NewGuid()));

        Assert.Equal(IdsAddressedTo(ModuleAudience.Everyone), offered);
    }

    /// <summary>A customer acting for no account is offered only what is addressed to everyone.</summary>
    /// <returns>Completes once the assertion has run.</returns>
    [Fact]
    public async Task A_customer_with_no_account_is_offered_only_the_modules_addressed_to_everyone()
    {
        var offered = await OfferedToAsync(new AccountlessCustomerCaller(Guid.NewGuid(), "acme"));

        Assert.Equal(IdsAddressedTo(ModuleAudience.Everyone), offered);
    }

    /// <summary>An anonymous caller is offered only what is addressed to everyone.</summary>
    /// <returns>Completes once the assertion has run.</returns>
    [Fact]
    public async Task An_anonymous_caller_is_offered_only_the_modules_addressed_to_everyone()
    {
        var offered = await OfferedToAsync(AnonymousCaller.Instance);

        Assert.Equal(IdsAddressedTo(ModuleAudience.Everyone), offered);
    }

    /// <summary>The ids of the registry's modules addressed to any of the audiences, in load order.</summary>
    /// <param name="audiences">The audiences to select.</param>
    /// <returns>The module ids.</returns>
    private static List<string> IdsAddressedTo(params ModuleAudience[] audiences)
    {
        return ModuleRegistry.All
            .Where(module => { return audiences.Contains(module.Manifest.Audience); })
            .Select(module => { return module.Manifest.Id; })
            .ToList();
    }

    /// <summary>Reads the catalogue as a given caller and returns the module names it lists.</summary>
    /// <param name="caller">The caller the host reports for the request.</param>
    /// <returns>The listed module names, in the order the endpoint returned them.</returns>
    private async Task<List<string>> OfferedToAsync(Caller caller)
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<ICurrentUser>(_ => { return new FixedCurrentUser(caller); });
            });
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/modules");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.EnumerateArray()
            .Select(module => { return module.GetProperty("name").GetString()!; })
            .ToList();
    }
}
