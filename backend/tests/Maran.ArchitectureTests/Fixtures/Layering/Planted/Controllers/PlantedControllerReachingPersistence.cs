using Maran.ArchitectureTests.Fixtures.Layering.Planted.Persistence;

namespace Maran.ArchitectureTests.Fixtures.Layering.Planted.Controllers;

/// <summary>
/// A planted controller that holds a persistence type: the refusing control for the Controllers rule.
/// </summary>
public sealed class PlantedControllerReachingPersistence
{
    /// <summary>The reference that breaks the layering.</summary>
    public PlantedPersistenceStore? Reached { get; init; }
}
