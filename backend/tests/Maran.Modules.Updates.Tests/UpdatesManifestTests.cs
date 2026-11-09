using Maran.Sdk.Contracts;

namespace Maran.Modules.Updates.Tests;

/// <summary>The module's identity: what it declares it needs, which is nothing from the agent.</summary>
public sealed class UpdatesManifestTests
{
    /// <summary>The module drives no part of the agent, owns no schema and is reachable by administrators only.</summary>
    [Fact]
    public void The_module_declares_no_agent_capability_no_schema_and_an_administrator_audience()
    {
        var manifest = new UpdatesModule().Manifest;

        Assert.Equal("updates", manifest.Id);
        Assert.Empty(manifest.AgentCapabilities);
        Assert.Null(manifest.SchemaName);
        Assert.Equal(ModuleAudience.AdministratorOnly, manifest.Audience);
        Assert.Equal(AuthorizationPolicies.AdminOnly, Assert.Single(manifest.Permissions));
    }
}
