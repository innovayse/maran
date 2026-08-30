using Maran.Host.Modules;

namespace Maran.ArchitectureTests;

/// <summary>
/// Every module publishes its own identity, and no two may collide. The resource key matters as
/// much as the id: all modules feed one shared resource pool, so two modules using the same key
/// makes one of them silently display the other's name — which is exactly what happened the first
/// time a second module shipped, and no test noticed until the catalogue was read in a browser.
/// </summary>
public sealed class ManifestUniquenessTests
{
    /// <summary>Every module has a distinct id.</summary>
    [Fact]
    public void Every_module_has_a_distinct_id()
    {
        var ids = ModuleRegistry.All.Select(module =>
        {
            return module.Manifest.Id;
        }).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>Every module has a distinct display name key.</summary>
    [Fact]
    public void Every_module_has_a_distinct_display_name_key()
    {
        var keys = ModuleRegistry.All.Select(module =>
        {
            return module.Manifest.DisplayNameKey;
        }).ToList();

        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>A modules display name key names the module it belongs to.</summary>
    [Fact]
    public void A_modules_display_name_key_names_the_module_it_belongs_to()
    {
        Assert.All(ModuleRegistry.All, module =>
        {
            Assert.StartsWith(module.Manifest.Id, module.Manifest.DisplayNameKey, StringComparison.OrdinalIgnoreCase);
        });
    }
}
