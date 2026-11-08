using Maran.Modules.Ssl.Persistence;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Ssl.Tests;

/// <summary>What the Ssl module's manifest states about the module, held to the code that backs it.</summary>
public sealed class SslManifestTests
{
    /// <summary>The module ships as packageable, so the panel treats its absence accordingly.</summary>
    [Fact]
    public void The_module_is_packageable()
    {
        Assert.Equal(ModuleKind.Packageable, SslManifest.Instance.Kind);
    }

    /// <summary>The schema the manifest names is the one the module's code actually owns.</summary>
    [Fact]
    public void The_manifest_names_the_schema_the_module_owns()
    {
        Assert.Equal(SslDbContext.SchemaName, SslManifest.Instance.SchemaName);
        Assert.Equal(SslManifest.Instance.Id, SslManifest.Instance.SchemaName);
    }

    /// <summary>The module declares no menu entry and no route, because its screens live in another module's.</summary>
    [Fact]
    public void The_module_declares_no_menu_entry_and_no_route()
    {
        Assert.Empty(SslManifest.Instance.Menu);
        Assert.Empty(SslManifest.Instance.Routes);
    }

    /// <summary>The module declares the one module it needs, by id and by a major range.</summary>
    [Fact]
    public void The_module_depends_on_sites_by_a_major_range()
    {
        var dependency = Assert.Single(SslManifest.Instance.Dependencies);

        Assert.Equal("sites", dependency.Id);
        Assert.Equal(">=1.0.0 <2.0.0", dependency.Range);
    }

    /// <summary>Within the module no menu key and no route name repeats.</summary>
    [Fact]
    public void No_menu_key_and_no_route_name_repeats_within_the_module()
    {
        var manifest = SslManifest.Instance;

        Assert.Equal(manifest.Menu.Count, manifest.Menu.Select(item =>
        {
            return item.Key;
        }).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(manifest.Routes.Count, manifest.Routes.Select(route =>
        {
            return route.Name;
        }).Distinct(StringComparer.Ordinal).Count());
    }
}
