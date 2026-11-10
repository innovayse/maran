using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests.Modules;

/// <summary>
/// A module the composition treats as one that arrived in a package: a manifest and nothing else. It
/// registers no service and serves no endpoint, because a host booted for the frame tests only has to
/// know that the module is composed, packaged and what state the loader reports for it.
/// </summary>
/// <remarks>
/// <b>It declares every agent capability, and that is the true declaration.</b> The composition's guard
/// judges a packaged module by the whole assembly its type lives in (rules/security.md item 13), and this
/// type lives in the integration assembly, whose stand-ins and harness take every agent client there is.
/// A manifest declaring fewer would be refused as reaching past it, which is the guard working: the
/// module would never be composed and the frame and catalogue tests would be about nothing.
/// </remarks>
public sealed class FakePackagedModule : IPanelModule
{
    /// <inheritdoc />
    public string Name
    {
        get { return Manifest.Id; }
    }

    /// <inheritdoc />
    public Manifest Manifest { get; }

    /// <summary>Creates the stand-in.</summary>
    /// <param name="id">The module id.</param>
    /// <param name="version">The module's version.</param>
    public FakePackagedModule(string id, string version)
    {
        Manifest = new Manifest(
            Id: id,
            DisplayNameKey: id + "DisplayName",
            Version: version,
            Tier: LicenceTier.Included,
            Kind: ModuleKind.Packageable,
            MinPanelVersion: "1.0.0",
            SchemaName: null,
            Dependencies: [],
            AgentCapabilities: [.. Enum.GetValues<AgentCapability>()],
            Permissions: [],
            Menu: [],
            Routes: [],
            Audience: ModuleAudience.Everyone);
    }

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Nothing to register: the module is composed only so the catalogue and the frame route know it.
    }
}
