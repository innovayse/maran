using Maran.ArchitectureTests.Fixtures.Layering.Planted.Queries;

namespace Maran.ArchitectureTests.Fixtures.Layering.Planted.Domain;

/// <summary>A planted Domain type that holds a query: the refusing control for the Domain rule.</summary>
public sealed class PlantedDomainReachingQueries
{
    /// <summary>The reference that breaks the layering.</summary>
    public PlantedLayerQuery? Reached { get; init; }
}
