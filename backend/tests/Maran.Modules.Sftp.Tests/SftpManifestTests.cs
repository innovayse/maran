using Maran.Modules.Sftp.Persistence;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Sftp.Tests;

/// <summary>What the Sftp module's manifest states about the module, held to the code that backs it.</summary>
public sealed class SftpManifestTests
{
    /// <summary>The module ships as packageable, so the panel treats its absence accordingly.</summary>
    [Fact]
    public void The_module_is_packageable()
    {
        Assert.Equal(ModuleKind.Packageable, SftpManifest.Instance.Kind);
    }

    /// <summary>The schema the manifest names is the one the module's code actually owns.</summary>
    [Fact]
    public void The_manifest_names_the_schema_the_module_owns()
    {
        Assert.Equal(SftpDbContext.SchemaName, SftpManifest.Instance.SchemaName);
        Assert.Equal(SftpManifest.Instance.Id, SftpManifest.Instance.SchemaName);
    }

    /// <summary>Every menu entry leads to a route the module declares, and the module declares some.</summary>
    [Fact]
    public void Every_menu_entry_leads_to_a_route_the_module_declares()
    {
        var manifest = SftpManifest.Instance;
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
        var manifest = SftpManifest.Instance;

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
