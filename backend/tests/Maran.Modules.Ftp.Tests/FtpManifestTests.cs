using Maran.Modules.Ftp.Persistence;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Ftp.Tests;

/// <summary>What the Ftp module's manifest states about the module, held to the code that backs it.</summary>
public sealed class FtpManifestTests
{
    /// <summary>The module ships as packageable, so the panel treats its absence accordingly.</summary>
    [Fact]
    public void The_module_is_packageable()
    {
        Assert.Equal(ModuleKind.Packageable, FtpManifest.Instance.Kind);
    }

    /// <summary>The schema the manifest names is the one the module's code actually owns.</summary>
    [Fact]
    public void The_manifest_names_the_schema_the_module_owns()
    {
        Assert.Equal(FtpDbContext.SchemaName, FtpManifest.Instance.SchemaName);
        Assert.Equal(FtpManifest.Instance.Id, FtpManifest.Instance.SchemaName);
    }

    /// <summary>Every menu entry leads to a route the module declares, and the module declares some.</summary>
    [Fact]
    public void Every_menu_entry_leads_to_a_route_the_module_declares()
    {
        var manifest = FtpManifest.Instance;
        var routeNames = manifest.Routes.Select(route =>
        {
            return route.Name;
        }).ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(manifest.Menu);
        Assert.All(manifest.Menu, item =>
        {
            Assert.Contains(item.RouteName, routeNames);
        });
    }

    /// <summary>Within the module no menu key and no route name repeats.</summary>
    [Fact]
    public void No_menu_key_and_no_route_name_repeats_within_the_module()
    {
        var manifest = FtpManifest.Instance;

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
