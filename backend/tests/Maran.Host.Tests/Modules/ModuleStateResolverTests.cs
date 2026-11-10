using Maran.Host.Modules;
using Maran.Host.Modules.Enums;
using Maran.Sdk.Contracts;

namespace Maran.Host.Tests.Modules;

/// <summary>Behavioral contract of <see cref="ModuleStateResolver"/>, the one decision point for a module's state.</summary>
public sealed class ModuleStateResolverTests
{
    /// <summary>A tier outside the enum is refused rather than guessed at.</summary>
    [Fact]
    public void A_tier_outside_the_enum_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            return ModuleStateResolver.Of((LicenceTier)99, EntitlementVerdict.Covered);
        });
    }

    /// <summary>A paid module follows the licence window's verdict for its tier: covered enables, lapsed lapses, anything else locks.</summary>
    [Theory]
    [InlineData(LicenceTier.AddOn, EntitlementVerdict.Covered, ModuleState.Enabled)]
    [InlineData(LicenceTier.AddOn, EntitlementVerdict.Lapsed, ModuleState.Lapsed)]
    [InlineData(LicenceTier.AddOn, EntitlementVerdict.NotCovered, ModuleState.Locked)]
    [InlineData(LicenceTier.PlanGated, EntitlementVerdict.Covered, ModuleState.Enabled)]
    [InlineData(LicenceTier.PlanGated, EntitlementVerdict.Lapsed, ModuleState.Lapsed)]
    [InlineData(LicenceTier.PlanGated, EntitlementVerdict.NotCovered, ModuleState.Locked)]
    public void A_paid_module_follows_the_verdict_of_the_licence_window(
        LicenceTier tier,
        EntitlementVerdict verdict,
        ModuleState expected)
    {
        Assert.Equal(expected, ModuleStateResolver.Of(tier, verdict));
    }

    /// <summary>
    /// An included module is enabled whatever the verdict says: it asks for no licence. The inverse control
    /// of the test above, which would pass a resolver that locked everything not covered.
    /// </summary>
    [Theory]
    [InlineData(EntitlementVerdict.Covered)]
    [InlineData(EntitlementVerdict.Lapsed)]
    [InlineData(EntitlementVerdict.NotCovered)]
    public void An_included_module_is_enabled_whatever_the_verdict_says(EntitlementVerdict verdict)
    {
        Assert.Equal(ModuleState.Enabled, ModuleStateResolver.Of(LicenceTier.Included, verdict));
    }
}
