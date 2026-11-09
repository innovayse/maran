using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Files.Tests;

/// <summary>What the Files module's manifest states about the module, held to the code that backs it.</summary>
public sealed class FilesManifestTests
{
    /// <summary>The module ships as core, so the panel treats its absence accordingly.</summary>
    [Fact]
    public void The_module_is_core()
    {
        Assert.Equal(ModuleKind.Core, FilesManifest.Instance.Kind);
    }

    /// <summary>The schema the manifest names is the one the module's code actually owns.</summary>
    [Fact]
    public void The_manifest_names_the_schema_the_module_owns()
    {
        Assert.Null(FilesManifest.Instance.SchemaName);
    }

    /// <summary>The module maps no database context, which is what a null schema claims (the inverse control).</summary>
    [Fact]
    public void The_module_assembly_maps_no_database_context()
    {
        var contexts = typeof(FilesManifest).Assembly.GetTypes().Where(type =>
        {
            return typeof(DbContext).IsAssignableFrom(type);
        }).ToList();

        Assert.Empty(contexts);
    }

    /// <summary>Every menu entry leads to a route the module declares, and the module declares some.</summary>
    [Fact]
    public void Every_menu_entry_leads_to_a_route_the_module_declares()
    {
        var manifest = FilesManifest.Instance;
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
        var manifest = FilesManifest.Instance;

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
