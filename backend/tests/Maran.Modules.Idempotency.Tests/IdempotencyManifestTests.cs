using Maran.Modules.Idempotency.Persistence;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Idempotency.Tests;

/// <summary>
/// What the Idempotency module's published identity claims about the schema it owns, who may call it, which
/// modules it needs, and the screens it contributes to the panel's navigation.
/// </summary>
public sealed class IdempotencyManifestTests
{
    /// <summary>The manifest's schema name is the module id and the schema the module's context owns.</summary>
    [Fact]
    public void The_manifest_names_the_schema_the_context_owns()
    {
        Assert.Equal("idempotency", IdempotencyManifest.Instance.Id);
        Assert.Equal(IdempotencyManifest.Instance.Id, IdempotencyManifest.Instance.SchemaName);
        Assert.Equal(IdempotencyDbContext.SchemaName, IdempotencyManifest.Instance.SchemaName);
    }

    /// <summary>The manifest states its kind, the policies its controllers use and the modules it requires.</summary>
    [Fact]
    public void The_manifest_states_its_kind_policies_and_requirements()
    {
        var manifest = IdempotencyManifest.Instance;

        Assert.Equal(ModuleKind.Core, manifest.Kind);
        Assert.Empty(manifest.Permissions);
        Assert.Empty(manifest.Dependencies);
    }

    /// <summary>The module has no screen, so it contributes no menu entry and no route.</summary>
    [Fact]
    public void The_module_contributes_no_menu_entry_and_no_route()
    {
        Assert.Empty(IdempotencyManifest.Instance.Menu);
        Assert.Empty(IdempotencyManifest.Instance.Routes);
    }
}
