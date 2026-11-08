using Maran.Modules.Fleet.Persistence;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Fleet.Tests;

/// <summary>
/// What the Fleet module's published identity claims about the schema it owns, which part of the agent it
/// may drive, who may call it, which modules it needs and the screens it contributes.
/// </summary>
public sealed class FleetManifestTests
{
    /// <summary>The manifest's schema name is the module id and the schema the module's context owns.</summary>
    [Fact]
    public void The_manifest_names_the_schema_the_context_owns()
    {
        Assert.Equal("fleet", FleetManifest.Instance.Id);
        Assert.Equal(FleetManifest.Instance.Id, FleetManifest.Instance.SchemaName);
        Assert.Equal(FleetDbContext.SchemaName, FleetManifest.Instance.SchemaName);
    }

    /// <summary>The module's published name is its manifest id, as the module contract requires.</summary>
    [Fact]
    public void The_module_name_is_the_manifest_id()
    {
        var module = new FleetModule();

        Assert.Equal(FleetManifest.Instance.Id, module.Name);
        Assert.Same(FleetManifest.Instance, module.Manifest);
    }

    /// <summary>The manifest states its kind, its administrator-only audience and the modules it requires.</summary>
    [Fact]
    public void The_manifest_states_its_kind_audience_and_requirements()
    {
        var manifest = FleetManifest.Instance;

        Assert.Equal(ModuleKind.Core, manifest.Kind);
        Assert.Equal(ModuleAudience.AdministratorOnly, manifest.Audience);
        Assert.Empty(manifest.Dependencies);
    }

    /// <summary>
    /// The module drives no part of the agent yet, so it declares none: a declaration ahead of its use
    /// would tell an administrator the module reaches a door it does not.
    /// </summary>
    [Fact]
    public void The_module_declares_no_agent_capability_until_it_uses_one()
    {
        Assert.Empty(FleetManifest.Instance.AgentCapabilities);
    }

    /// <summary>The module has no endpoint and no screen yet, so it contributes no permission, menu entry or route.</summary>
    [Fact]
    public void The_module_contributes_no_permission_menu_entry_or_route_yet()
    {
        Assert.Empty(FleetManifest.Instance.Permissions);
        Assert.Empty(FleetManifest.Instance.Menu);
        Assert.Empty(FleetManifest.Instance.Routes);
    }
}
