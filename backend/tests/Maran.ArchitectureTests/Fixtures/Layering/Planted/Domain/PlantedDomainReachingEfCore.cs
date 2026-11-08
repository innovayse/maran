using Microsoft.EntityFrameworkCore;

namespace Maran.ArchitectureTests.Fixtures.Layering.Planted.Domain;

/// <summary>A planted Domain type that holds an EF Core type: the refusing control for the Domain rule.</summary>
public sealed class PlantedDomainReachingEfCore
{
    /// <summary>The reference that breaks the layering.</summary>
    public DbContext? Reached { get; init; }
}
