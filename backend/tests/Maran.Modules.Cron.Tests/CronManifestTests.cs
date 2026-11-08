using System.Globalization;
using System.Resources;
using Maran.Modules.Cron.Persistence;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Cron.Tests;

/// <summary>
/// What the Cron module's published identity claims about the schema it owns, who may call it, which
/// modules it needs, and the screens it contributes to the panel's navigation.
/// </summary>
public sealed class CronManifestTests
{
    /// <summary>The manifest's schema name is the module id and the schema the module's context owns.</summary>
    [Fact]
    public void The_manifest_names_the_schema_the_context_owns()
    {
        Assert.Equal("cron", CronManifest.Instance.Id);
        Assert.Equal(CronManifest.Instance.Id, CronManifest.Instance.SchemaName);
        Assert.Equal(CronDbContext.SchemaName, CronManifest.Instance.SchemaName);
    }

    /// <summary>The manifest states its kind, the policies its controllers use and the modules it requires.</summary>
    [Fact]
    public void The_manifest_states_its_kind_policies_and_requirements()
    {
        var manifest = CronManifest.Instance;

        Assert.Equal(ModuleKind.Packageable, manifest.Kind);
        Assert.Equal([AuthorizationPolicies.AnyAuthenticated], manifest.Permissions);
        Assert.Empty(manifest.Dependencies);
    }

    /// <summary>Every menu entry opens a route that the same manifest declares, and a route exists.</summary>
    [Fact]
    public void Every_menu_entry_opens_a_route_the_manifest_declares()
    {
        var manifest = CronManifest.Instance;
        var routeNames = manifest.Routes.Select(route =>
        {
            return route.Name;
        }).ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(manifest.Routes);
        foreach (var entry in manifest.Menu)
        {
            Assert.Contains(entry.RouteName, routeNames);
        }
    }

    /// <summary>A menu key sits under the module's own id, and no route is named twice.</summary>
    [Fact]
    public void Menu_keys_sit_under_the_module_id_and_route_names_are_unique()
    {
        var manifest = CronManifest.Instance;

        foreach (var entry in manifest.Menu)
        {
            Assert.True(
                entry.Key == manifest.Id || entry.Key.StartsWith(manifest.Id + "-", StringComparison.Ordinal),
                $"Menu key '{entry.Key}' is not under the module id '{manifest.Id}'.");
        }

        Assert.Equal(
            manifest.Routes.Count,
            manifest.Routes.Select(route =>
            {
                return route.Name;
            }).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// Every menu label and route title is defined in the module's display names in English, Russian and
    /// Armenian, and the lookup does see a key that is missing.
    /// </summary>
    [Fact]
    public void Every_menu_label_and_route_title_is_defined_in_english_russian_and_armenian()
    {
        var manifest = CronManifest.Instance;
        var table = new ResourceManager(
            "Maran.Modules.Cron.Resources.DisplayNames",
            typeof(CronModule).Assembly);
        var keys = manifest.Menu.Select(entry =>
        {
            return entry.LabelKey;
        }).Concat(manifest.Routes.Select(route =>
        {
            return route.TitleKey;
        })).ToList();

        Assert.NotEmpty(keys);
        foreach (var culture in new[] { CultureInfo.InvariantCulture, new CultureInfo("ru"), new CultureInfo("hy") })
        {
            var set = table.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
            Assert.NotNull(set);
            Assert.Null(set.GetString("NoSuchDisplayNameKey"));
            foreach (var key in keys)
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(set.GetString(key)),
                    $"'{key}' has no text in '{culture.Name}'.");
            }
        }
    }
}
