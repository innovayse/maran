using Maran.ArchitectureTests.Fixtures;
using Maran.Host.Modules;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.ArchitectureTests;

/// <summary>
/// A module that arrives in a package is held to the same agent-capability declaration as one that ships
/// with the panel (rules/security.md item 13): the composition refuses it, and does not stop the panel.
/// </summary>
/// <remarks>
/// <para>
/// The refusing case is a candidate from this test assembly, which holds
/// <see cref="UndeclaredAgentModule"/> - a type that asks for an agent client its manifest never
/// declares. <c>AgentCapabilityGuard</c> reads a whole assembly, so any module whose type lives here
/// is refused for it; that is the planted violation, and nothing is composed from it. The accepting
/// control is a shipped module from an assembly of its own, offered as if it had arrived in a package:
/// without it, a guard mutated to refuse every candidate would pass the refusing case alone.
/// </para>
/// <para>
/// UNOBSERVED HERE: a module that locates an agent client through <see cref="IServiceProvider"/> at run
/// time declares nothing in its signatures and is not caught (rules/security.md item 13, "Stated limit").
/// </para>
/// </remarks>
public sealed class PackagedModuleCapabilityTests
{
    /// <summary>A packaged module whose assembly reaches the agent without declaring it is refused and not composed.</summary>
    [Fact]
    public void A_packaged_module_whose_assembly_reaches_the_agent_without_declaring_it_is_refused()
    {
        var undeclared = new PlantedBadManifestModule(
            "packaged", PlantedBadManifestModule.Valid("packaged", ModuleKind.Packageable));

        var composition = PackagedModuleComposer.Compose(
            ModuleRegistry.All, [undeclared], new StatedPanelVersion("99.0.0"));

        var refusal = Assert.Single(composition.Refused);
        Assert.Equal("ModuleAgentCapabilityUndeclared", refusal.Reason.Code);
        Assert.Equal(ModuleRegistry.All.Count, composition.Composed.Count);
    }

    /// <summary>An honest packaged module, declaring what it uses, is composed (accepting control).</summary>
    [Fact]
    public void A_packaged_module_that_declares_what_it_uses_is_composed()
    {
        var shipped = ModuleRegistry.All.Where(module => { return module.Manifest.Id != "firewall"; }).ToList();
        var firewall = ModuleRegistry.All.Single(module => { return module.Manifest.Id == "firewall"; });
        Assert.Contains(AgentCapability.Firewall, firewall.Manifest.AgentCapabilities);

        var composition = PackagedModuleComposer.Compose(shipped, [firewall], new StatedPanelVersion("99.0.0"));

        Assert.Empty(composition.Refused);
        Assert.Contains(firewall, composition.Composed);
    }

    /// <summary>A panel version a test states.</summary>
    private sealed class StatedPanelVersion : IPanelVersion
    {
        /// <summary>Creates the stand-in.</summary>
        /// <param name="current">The version it reports.</param>
        public StatedPanelVersion(string current)
        {
            Current = current;
        }

        /// <inheritdoc />
        public string Current { get; }
    }
}
