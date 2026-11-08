namespace Maran.ArchitectureTests.Fixtures.Layering.Clean.Domain;

/// <summary>
/// A clean Domain entity that depends on a sibling Domain type only: the accepting control for the Domain rule.
/// </summary>
public sealed class CleanDomainEntity
{
    /// <summary>The sibling Domain value, which the layering law allows.</summary>
    public CleanDomainValue? Value { get; init; }
}
