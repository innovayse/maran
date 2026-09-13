using Maran.Agent.Client.Interfaces;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.ArchitectureTests.Fixtures;

/// <summary>
/// A module that takes an agent client and declares no capability at all — the positive control for
/// <c>AgentCapabilityGuard</c>.
/// </summary>
/// <remarks>
/// <para>
/// It exists because the guard's real assertion is a negative one: every module in the tree passes,
/// and a guard that had stopped detecting anything would pass just as quietly. This module is the
/// value the probe must find. Its constructor asks for <see cref="IAgentSitesClient"/> — the
/// ordinary way any module reaches the agent — while its manifest declares nothing, which is
/// exactly the shape a marketplace module would take to acquire root access it never advertised.
/// </para>
/// <para>
/// The dependency is a real constructor parameter rather than a mention in a comment, because that
/// is what the guard reads: a control that faked the shape would prove the guard finds fakes.
/// Nothing constructs this type — it is inspected, never composed — so the client it asks for is
/// never resolved.
/// </para>
/// </remarks>
public sealed class UndeclaredAgentModule : IPanelModule
{
    /// <summary>The client this module would drive, and the reason the guard must refuse it.</summary>
    private readonly IAgentSitesClient _sites;

    /// <summary>Creates the control, taking the dependency it fails to declare.</summary>
    /// <param name="sites">The agent's sites contract, asked for the way every real module asks.</param>
    public UndeclaredAgentModule(IAgentSitesClient sites)
    {
        _sites = sites;
    }

    /// <inheritdoc />
    public string Name
    {
        get
        {
            return "undeclared";
        }
    }

    /// <inheritdoc />
    public Manifest Manifest { get; } = new(
        Id: "undeclared",
        DisplayNameKey: "UndeclaredModuleDisplayName",
        Version: "0.0.0",
        Tier: LicenceTier.Included,
        Dependencies: [],
        AgentCapabilities: []);

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Never called: this type is inspected by the guard, never composed into a panel.
    }

    /// <summary>Names the client this control holds, so the field is read and not merely stored.</summary>
    /// <returns>The runtime type name of the agent client it depends on.</returns>
    public string DependencyName()
    {
        return _sites.GetType().Name;
    }
}
