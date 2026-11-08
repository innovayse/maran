using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Licensing.Tests;

/// <summary>What the Licensing module's manifest states about the module, held to the code that backs it.</summary>
public sealed class LicensingManifestTests
{
    /// <summary>The module ships as core, so the panel treats its absence accordingly.</summary>
    [Fact]
    public void The_module_is_core()
    {
        Assert.Equal(ModuleKind.Core, LicensingManifest.Instance.Kind);
    }

    /// <summary>The schema the manifest names is the one the module's code actually owns.</summary>
    [Fact]
    public void The_manifest_names_the_schema_the_module_owns()
    {
        Assert.Null(LicensingManifest.Instance.SchemaName);
    }

    /// <summary>The module maps no database context, which is what a null schema claims (the inverse control).</summary>
    [Fact]
    public void The_module_assembly_maps_no_database_context()
    {
        var contexts = typeof(LicensingManifest).Assembly.GetTypes().Where(type =>
        {
            return typeof(DbContext).IsAssignableFrom(type);
        }).ToList();

        Assert.Empty(contexts);
    }

    /// <summary>The module declares no menu entry and no route, because its screens live in another module's.</summary>
    [Fact]
    public void The_module_declares_no_menu_entry_and_no_route()
    {
        Assert.Empty(LicensingManifest.Instance.Menu);
        Assert.Empty(LicensingManifest.Instance.Routes);
    }

    /// <summary>Within the module no menu key and no route name repeats.</summary>
    [Fact]
    public void No_menu_key_and_no_route_name_repeats_within_the_module()
    {
        var manifest = LicensingManifest.Instance;

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
