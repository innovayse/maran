using Maran.Sdk.Contracts;
using Maran.Sdk.Navigation;

namespace Maran.Samples.Hello;

/// <summary>
/// Holds the sample module's single published <see cref="Manifest"/>. It is the reference for a module
/// author: every member is stated, and each states the smallest honest thing.
/// </summary>
public static class HelloManifest
{
    /// <summary>The sample module's published identity.</summary>
    /// <remarks>
    /// <see cref="ModuleKind.Packageable"/>: it can be installed from a package. It owns the
    /// <c>hello</c> schema, so its id and its schema name are the same word. It declares no agent
    /// capability because it never drives the agent, and one permission because its one controller
    /// is administrator-only. <see cref="LicenceTier.AddOn"/> makes the sample a paid-shaped module, so
    /// the loader suites exercise the entitlement path with it.
    /// </remarks>
    public static Manifest Instance { get; } = new(
        Id: "hello",
        DisplayNameKey: "HelloModuleDisplayName",
        Version: "1.0.0",
        Tier: LicenceTier.AddOn,
        Kind: ModuleKind.Packageable,
        MinPanelVersion: "1.0.0-beta.1",
        SchemaName: "hello",
        Dependencies: [],
        AgentCapabilities: [],
        Permissions: [AuthorizationPolicies.AdminOnly],
        Menu: [
            new ModuleMenuItem(
                Key: "hello",
                RouteName: "hello",
                IconKey: ModuleIconKeys.Grid,
                LabelKey: "HelloMenuHello",
                Order: 900,
                Audience: ModuleAudience.AdministratorOnly),
        ],
        Routes: [
            new ModuleRoute(
                Name: "hello",
                Path: "/hello",
                TitleKey: "HelloRouteHello",
                RequiresModule: null),
        ],
        Audience: ModuleAudience.AdministratorOnly);
}
