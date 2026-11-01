using Xunit.Abstractions;
using Xunit.Sdk;

namespace Maran.Host.IntegrationTests.Traits;

/// <summary>
/// Turns <see cref="PerformanceBudgetAttribute"/> into the <c>Category=PerformanceBudget</c> trait that
/// VSTest filters see.
/// </summary>
public sealed class PerformanceBudgetTraitDiscoverer : ITraitDiscoverer
{
    /// <summary>Yields the one trait the attribute stands for.</summary>
    /// <param name="traitAttribute">The attribute found on the test.</param>
    /// <returns>The category trait.</returns>
    public IEnumerable<KeyValuePair<string, string>> GetTraits(IAttributeInfo traitAttribute)
    {
        yield return new KeyValuePair<string, string>(
            PerformanceBudgetAttribute.TraitName,
            PerformanceBudgetAttribute.TraitValue);
    }
}
