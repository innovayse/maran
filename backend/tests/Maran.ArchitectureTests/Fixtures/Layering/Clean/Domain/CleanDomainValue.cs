namespace Maran.ArchitectureTests.Fixtures.Layering.Clean.Domain;

/// <summary>A clean Domain value: it depends on nothing in the layering law's forbidden list.</summary>
public sealed class CleanDomainValue
{
    /// <summary>The value held.</summary>
    public int Amount { get; init; }
}
