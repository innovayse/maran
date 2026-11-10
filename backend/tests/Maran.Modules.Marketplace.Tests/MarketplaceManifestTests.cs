using Maran.Modules.Marketplace.Options;
using Maran.Modules.Marketplace.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Marketplace.Tests;

/// <summary>What the Marketplace manifest states about the module, held to the code that backs it.</summary>
public sealed class MarketplaceManifestTests
{
    /// <summary>The module is a core, included, administrator-only part of the panel.</summary>
    [Fact]
    public void The_module_is_core_included_and_for_administrators_only()
    {
        var manifest = MarketplaceManifest.Instance;

        Assert.Equal("marketplace", manifest.Id);
        Assert.Equal(ModuleKind.Core, manifest.Kind);
        Assert.Equal(LicenceTier.Included, manifest.Tier);
        Assert.Equal(ModuleAudience.AdministratorOnly, manifest.Audience);
        Assert.Equal([AuthorizationPolicies.AdminOnly], manifest.Permissions);
    }

    /// <summary>The module never touches the agent, so it declares no capability.</summary>
    [Fact]
    public void The_module_declares_no_agent_capability()
    {
        Assert.Empty(MarketplaceManifest.Instance.AgentCapabilities);
    }

    /// <summary>The schema the manifest names is the one the module's code actually owns, and equals the id.</summary>
    [Fact]
    public void The_manifest_names_the_schema_the_module_owns()
    {
        Assert.Equal(MarketplaceDbContext.SchemaName, MarketplaceManifest.Instance.SchemaName);
        Assert.Equal(MarketplaceManifest.Instance.Id, MarketplaceManifest.Instance.SchemaName);
    }

    /// <summary>The module's screen is a page of the core SPA, so the manifest adds no menu entry and no route.</summary>
    [Fact]
    public void The_manifest_adds_no_menu_entry_and_no_route()
    {
        Assert.Empty(MarketplaceManifest.Instance.Menu);
        Assert.Empty(MarketplaceManifest.Instance.Routes);
    }

    /// <summary>The module's name is its manifest's id, which is how the registry and the catalogue address it.</summary>
    [Fact]
    public void The_module_is_named_by_its_manifest_id()
    {
        var module = new MarketplaceModule();

        Assert.Equal("marketplace", module.Name);
        Assert.Same(MarketplaceManifest.Instance, module.Manifest);
    }

    /// <summary>Composing the module registers no start-up work, and a bad address fails on first read of the options and not before.</summary>
    [Fact]
    public void Composing_the_module_registers_no_start_up_work_and_validates_the_options_lazily()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Panel"] = "Host=localhost;Database=unused",
                ["Marketplace:BaseUrl"] = "http://not-https.example.org",
            })
            .Build();
        var services = new ServiceCollection();

        new MarketplaceModule().ConfigureServices(services, configuration);
        using var provider = services.BuildServiceProvider();

        Assert.DoesNotContain(services, descriptor => { return descriptor.ServiceType == typeof(IHostedService); });
        Assert.NotNull(provider.GetService<IModuleMigrations>());
        var options = provider.GetRequiredService<IOptions<MarketplaceOptions>>();
        Assert.Throws<OptionsValidationException>(() => { _ = options.Value; });
    }

    /// <summary>The inverse control: with the default address the same composition reads its options without complaint.</summary>
    [Fact]
    public void Composing_the_module_with_the_default_address_reads_the_options_without_complaint()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Panel"] = "Host=localhost;Database=unused",
            })
            .Build();
        var services = new ServiceCollection();

        new MarketplaceModule().ConfigureServices(services, configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Equal(
            MarketplaceOptions.DefaultBaseUrl,
            provider.GetRequiredService<IOptions<MarketplaceOptions>>().Value.BaseUrl);
    }
}
