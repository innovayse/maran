using Maran.ArchitectureTests.Fixtures.Layering.Planted.Controllers;

namespace Maran.ArchitectureTests.Fixtures.Layering.Planted.Domain;

/// <summary>A planted Domain type that holds a controller: the refusing control for the Domain rule.</summary>
public sealed class PlantedDomainReachingController
{
    /// <summary>The reference that breaks the layering.</summary>
    public PlantedLayerController? Reached { get; init; }
}
