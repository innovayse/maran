using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Domain.Policies;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Licensing.Tests.Domain.Policies;

/// <summary>
/// Covers <see cref="CoverageTransitionPolicy"/>, the one rule that says which changes of a module's
/// cover are journalled: the start of a lapse and the end of one, each beside the changes that must
/// stay silent.
/// </summary>
public sealed class CoverageTransitionPolicyTests
{
    /// <summary>A module whose cover ran out, or that was found already lapsed, is a lapse.</summary>
    [Theory]
    [InlineData(EntitlementVerdict.Covered)]
    [InlineData(EntitlementVerdict.NotCovered)]
    [InlineData(null)]
    public void A_module_that_is_lapsed_and_was_not_is_a_lapse(EntitlementVerdict? previous)
    {
        Assert.Equal(CoverageTransition.Lapsed, CoverageTransitionPolicy.Classify(previous, EntitlementVerdict.Lapsed));
    }

    /// <summary>A module that stays lapsed is not journalled again; the inverse control of the lapse case.</summary>
    [Fact]
    public void A_module_that_stays_lapsed_is_not_a_new_lapse()
    {
        Assert.Equal(
            CoverageTransition.None,
            CoverageTransitionPolicy.Classify(EntitlementVerdict.Lapsed, EntitlementVerdict.Lapsed));
    }

    /// <summary>A lapsed module that is covered again is a recovery.</summary>
    [Fact]
    public void A_lapsed_module_that_is_covered_again_is_a_recovery()
    {
        Assert.Equal(
            CoverageTransition.Recovered,
            CoverageTransitionPolicy.Classify(EntitlementVerdict.Lapsed, EntitlementVerdict.Covered));
    }

    /// <summary>A module covered for the first time, or still covered, is nothing to journal here.</summary>
    [Theory]
    [InlineData(EntitlementVerdict.Covered)]
    [InlineData(EntitlementVerdict.NotCovered)]
    [InlineData(null)]
    public void A_covered_module_that_did_not_lapse_is_not_journalled(EntitlementVerdict? previous)
    {
        Assert.Equal(CoverageTransition.None, CoverageTransitionPolicy.Classify(previous, EntitlementVerdict.Covered));
    }

    /// <summary>A module that stops being covered without having lapsed, or that a lapse ended by dropping it, is not journalled.</summary>
    [Theory]
    [InlineData(EntitlementVerdict.Covered)]
    [InlineData(EntitlementVerdict.Lapsed)]
    [InlineData(null)]
    public void A_module_that_is_not_covered_is_not_journalled(EntitlementVerdict? previous)
    {
        Assert.Equal(CoverageTransition.None, CoverageTransitionPolicy.Classify(previous, EntitlementVerdict.NotCovered));
    }
}
