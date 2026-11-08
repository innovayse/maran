using Maran.ArchitectureTests.Fixtures.Layering.Planted.Persistence;

namespace Maran.ArchitectureTests.Fixtures.Layering.Planted.Domain;

/// <summary>A planted Domain type that holds a persistence type: the refusing control for the Domain rule.</summary>
public sealed class PlantedDomainReachingPersistence
{
    /// <summary>The reference that breaks the layering.</summary>
    public PlantedPersistenceStore? Reached { get; init; }
}
