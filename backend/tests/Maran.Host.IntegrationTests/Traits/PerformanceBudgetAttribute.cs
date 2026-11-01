using Xunit.Sdk;

namespace Maran.Host.IntegrationTests.Traits;

/// <summary>
/// Marks a test class or method as a performance budget: it times the panel over a large seeded
/// dataset rather than asserting a behaviour, so it runs on the push to <c>dev</c> and nightly lane
/// and not on every pull request (issue #289).
/// </summary>
/// <remarks>
/// It is a typed trait rather than a <c>[Trait("Category", "...")]</c> string so the category is spelled
/// once, here, and a test is selected by what it is rather than by how it happens to be named. The
/// harness selects it with the VSTest filter <c>Category=PerformanceBudget</c> (scripts/lib/suite.sh)
/// and scores it against its own baseline row, so moving it off the pull request path does not take
/// it out of the census.
/// </remarks>
[TraitDiscoverer(
    "Maran.Host.IntegrationTests.Traits.PerformanceBudgetTraitDiscoverer",
    "Maran.Host.IntegrationTests")]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class PerformanceBudgetAttribute : Attribute, ITraitAttribute
{
    /// <summary>The trait name the harness filters on.</summary>
    public const string TraitName = "Category";

    /// <summary>The trait value the harness filters on.</summary>
    public const string TraitValue = "PerformanceBudget";
}
