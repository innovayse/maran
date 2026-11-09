using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.Sdk.Navigation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.ArchitectureTests.Fixtures;

/// <summary>
/// A module whose manifest and name are whatever a test hands it — the planted violation every
/// contract rule is shown to fail on, and, built from <see cref="Valid"/>, the accepting control
/// each of them is shown to pass.
/// </summary>
/// <remarks>
/// <para>
/// It exists because the contract tests assert a NEGATIVE over the real modules: nothing is wrong,
/// and a law that had stopped looking would report exactly that. A rule is proven by handing it a
/// module that breaks it and watching it name the rule; the accepting control is the same module
/// with the defect removed, because a law mutated to refuse everything passes every test that only
/// ever hands it broken input.
/// </para>
/// <para>
/// Nothing composes this type: it is read through <c>ModuleContractCensus.SubjectOf</c>, the same
/// path a real module takes, so a planted defect is judged exactly like a real one. Its controller
/// policies are handed in (none), never discovered, because its assembly is the test project and the
/// fixture controllers there belong to other tests.
/// </para>
/// </remarks>
public sealed class PlantedBadManifestModule : IPanelModule
{
    /// <summary>Creates the planted module.</summary>
    /// <param name="name">What <see cref="Name"/> answers, which a rule compares with the manifest's id.</param>
    /// <param name="manifest">The manifest the module publishes.</param>
    public PlantedBadManifestModule(string name, Manifest manifest)
    {
        Name = name;
        Manifest = manifest;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public Manifest Manifest { get; }

    /// <summary>A manifest that obeys every rule of the contract.</summary>
    /// <param name="id">The module id, which is also its name and its schema.</param>
    /// <param name="kind">Whether the module is core or packageable.</param>
    /// <returns>A manifest with one menu entry, the route it points at and one real policy.</returns>
    public static Manifest Valid(string id, ModuleKind kind)
    {
        return new Manifest(
            Id: id,
            DisplayNameKey: "PlantedModuleDisplayName",
            Version: "1.0.0",
            Tier: LicenceTier.Included,
            Kind: kind,
            MinPanelVersion: "1.0.0",
            SchemaName: id,
            Dependencies: [],
            AgentCapabilities: [],
            Permissions: [AuthorizationPolicies.AdminOnly],
            Menu: [new ModuleMenuItem(
                Key: id,
                RouteName: id,
                IconKey: "server",
                LabelKey: "PlantedModuleMenuLabel",
                Order: 100,
                Audience: ModuleAudience.AdministratorOnly)],
            Routes: [new ModuleRoute(
                Name: id,
                Path: "/" + id,
                TitleKey: "PlantedModuleRouteTitle",
                RequiresModule: null)],
            Audience: ModuleAudience.AdministratorOnly);
    }

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Never called: this type is read by the contract law, never composed into a panel.
    }
}
