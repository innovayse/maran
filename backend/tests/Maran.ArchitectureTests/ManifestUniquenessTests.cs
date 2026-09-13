using System.Xml.Linq;
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

    /// <summary>Every modules display name key has an entry in a resource file.</summary>
    /// <remarks>
    /// A <c>DisplayNameKey</c> naming no resource entry is not a build error and not a runtime
    /// error: <c>ResourceManager</c> answers with the key itself, so the module catalogue quietly
    /// renders a raw identifier like <c>SitesModuleDisplayName</c> to every customer. The resx files
    /// are read as the source of truth, the same way <see cref="ResourceKeyParityTests"/> does, so a
    /// key deleted from a file is caught in the file the author edited.
    /// </remarks>
    [Fact]
    public void Every_modules_display_name_key_has_an_entry_in_a_resource_file()
    {
        var declaredKeys = AllResourceKeys();
        Assert.NotEmpty(declaredKeys);

        var missing = ModuleRegistry.All
            .Select(module =>
            {
                return module.Manifest.DisplayNameKey;
            })
            .Where(key =>
            {
                return !declaredKeys.Contains(key);
            })
            .ToList();

        Assert.True(
            missing.Count == 0,
            "These module display-name keys have no .resx entry, so the catalogue would show the raw "
            + string.Join(", ", missing) + ".");
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

    /// <summary>Reads every key declared by every authored .resx file under backend/src.</summary>
    /// <returns>The full set of resource keys the panel ships.</returns>
    private static HashSet<string> AllResourceKeys()
    {
        var sourceRoot = Path.Combine(FindBackendRoot(), "src");
        return Directory.EnumerateFiles(sourceRoot, "*.resx", SearchOption.AllDirectories)
            .Where(path =>
            {
                return !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal);
            })
            .SelectMany(path =>
            {
                return XDocument.Load(path).Root!.Elements("data")
                    .Select(element =>
                    {
                        return element.Attribute("name")?.Value;
                    });
            })
            .Where(name =>
            {
                return !string.IsNullOrEmpty(name);
            })
            .Select(name =>
            {
                return name!;
            })
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Walks up from the test binary to the folder holding <c>Maran.sln</c>.</summary>
    /// <returns>Absolute path of the backend root.</returns>
    private static string FindBackendRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Maran.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Maran.sln not found above the test output directory.");
    }
}
