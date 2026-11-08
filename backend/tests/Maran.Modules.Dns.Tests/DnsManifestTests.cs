using Maran.Modules.Dns.Persistence;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Dns.Tests;

/// <summary>What the Dns module's manifest states about the module, held to the code that backs it.</summary>
public sealed class DnsManifestTests
{
    /// <summary>The module ships as packageable and declares exactly the DNS part of the agent.</summary>
    [Fact]
    public void The_module_is_packageable_and_declares_only_the_dns_capability()
    {
        Assert.Equal(ModuleKind.Packageable, DnsManifest.Instance.Kind);
        Assert.Equal([AgentCapability.Dns], DnsManifest.Instance.AgentCapabilities);
    }

    /// <summary>The schema the manifest names is the one the module's code actually owns.</summary>
    [Fact]
    public void The_manifest_names_the_schema_the_module_owns()
    {
        Assert.Equal(DnsDbContext.SchemaName, DnsManifest.Instance.SchemaName);
        Assert.Equal(DnsManifest.Instance.Id, DnsManifest.Instance.SchemaName);
    }

    /// <summary>Every menu entry leads to a route the module declares, and keys do not repeat.</summary>
    [Fact]
    public void Every_menu_entry_leads_to_a_declared_route_and_no_key_repeats()
    {
        var manifest = DnsManifest.Instance;
        var routeNames = manifest.Routes.Select(route =>
        {
            return route.Name;
        }).ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(manifest.Menu);
        Assert.All(manifest.Menu, item =>
        {
            Assert.Contains(item.RouteName, routeNames);
        });
        Assert.Equal(manifest.Menu.Count, manifest.Menu.Select(item =>
        {
            return item.Key;
        }).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>The name-server settings entry is for administrators only; no entry is offered to a reseller.</summary>
    [Fact]
    public void The_settings_entry_is_for_administrators_only()
    {
        var settings = DnsManifest.Instance.Menu.Single(item =>
        {
            return item.Key == "dns-nameservers";
        });

        Assert.Equal(ModuleAudience.AdministratorOnly, settings.Audience);
        Assert.DoesNotContain(DnsManifest.Instance.Menu, item =>
        {
            return item.Audience == ModuleAudience.AdminOrReseller;
        });
    }
}
