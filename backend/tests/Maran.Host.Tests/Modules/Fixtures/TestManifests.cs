using Maran.Sdk.Contracts;
using Maran.Sdk.Navigation;

namespace Maran.Host.Tests.Modules.Fixtures;

/// <summary>Builds the smallest valid <see cref="Manifest"/> a test needs, with only the members it varies.</summary>
internal static class TestManifests
{
    /// <summary>Builds a manifest for a module with no schema, no capabilities and no screens.</summary>
    /// <param name="id">The module id.</param>
    /// <param name="version">The module's version.</param>
    /// <param name="dependencies">The modules it declares a dependency on.</param>
    /// <param name="menu">The menu entries it contributes.</param>
    /// <returns>The manifest.</returns>
    public static Manifest Of(
        string id,
        string version = "1.0.0",
        IReadOnlyList<ModuleDependency>? dependencies = null,
        IReadOnlyList<ModuleMenuItem>? menu = null)
    {
        return new Manifest(
            Id: id,
            DisplayNameKey: id + "DisplayName",
            Version: version,
            Tier: LicenceTier.Included,
            Kind: ModuleKind.Packageable,
            MinPanelVersion: "1.0.0",
            SchemaName: null,
            Dependencies: dependencies ?? [],
            AgentCapabilities: [],
            Permissions: [],
            Menu: menu ?? [],
            Routes: [],
            Audience: ModuleAudience.Everyone);
    }
}
