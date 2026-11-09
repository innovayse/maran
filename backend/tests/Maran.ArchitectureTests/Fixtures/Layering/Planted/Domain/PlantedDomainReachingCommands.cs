using Maran.ArchitectureTests.Fixtures.Layering.Planted.Commands;

namespace Maran.ArchitectureTests.Fixtures.Layering.Planted.Domain;

/// <summary>A planted Domain type that holds a command: the refusing control for the Domain rule.</summary>
public sealed class PlantedDomainReachingCommands
{
    /// <summary>The reference that breaks the layering.</summary>
    public PlantedLayerCommand? Reached { get; init; }
}
