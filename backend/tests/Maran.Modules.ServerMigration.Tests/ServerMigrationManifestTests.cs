using Maran.Modules.ServerMigration.Persistence;
using Maran.Sdk.Contracts;

namespace Maran.Modules.ServerMigration.Tests;

/// <summary>
/// What the ServerMigration module's published identity claims: the schema it owns, the one part of the agent it may
/// drive, and that only an administrator is offered it.
/// </summary>
public sealed class ServerMigrationManifestTests
{
    /// <summary>The manifest's schema is the module id and the schema the module's context owns.</summary>
    [Fact]
    public void The_manifest_names_the_schema_the_context_owns()
    {
        Assert.Equal("servermigration", ServerMigrationManifest.Instance.Id);
        Assert.Equal(ServerMigrationManifest.Instance.Id, ServerMigrationManifest.Instance.SchemaName);
        Assert.Equal(ServerMigrationDbContext.SchemaName, ServerMigrationManifest.Instance.SchemaName);
    }

    /// <summary>The module's published name is its manifest id.</summary>
    [Fact]
    public void The_module_name_is_the_manifest_id()
    {
        var module = new ServerMigrationModule();

        Assert.Equal(ServerMigrationManifest.Instance.Id, module.Name);
        Assert.Same(ServerMigrationManifest.Instance, module.Manifest);
    }

    /// <summary>The module may drive the outbound transfer part of the agent and nothing else.</summary>
    [Fact]
    public void The_module_declares_the_transfer_capability_and_no_other()
    {
        Assert.Equal([AgentCapability.Transfer], ServerMigrationManifest.Instance.AgentCapabilities);
    }

    /// <summary>
    /// Everything the module offers is for an administrator: the module, its menu entries and its policy. The one other
    /// policy is the export grant's, which admits a live grant and no login.
    /// </summary>
    [Fact]
    public void Everything_the_module_offers_is_administrator_only()
    {
        var manifest = ServerMigrationManifest.Instance;

        Assert.Equal(ModuleAudience.AdministratorOnly, manifest.Audience);
        Assert.Equal([AuthorizationPolicies.AdminOnly, MigrationGrantPolicyNames.Pull], manifest.Permissions);
        Assert.NotEmpty(manifest.Menu);
        Assert.All(manifest.Menu, entry => { Assert.Equal(ModuleAudience.AdministratorOnly, entry.Audience); });
        Assert.Equal(
            manifest.Menu.Select(entry => { return entry.RouteName; }).Order(StringComparer.Ordinal),
            manifest.Routes.Select(route => { return route.Name; }).Order(StringComparer.Ordinal));
        Assert.Equal(ModuleKind.Core, manifest.Kind);
        Assert.Empty(manifest.Dependencies);
    }
}
