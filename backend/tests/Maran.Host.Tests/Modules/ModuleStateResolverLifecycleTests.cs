using Maran.Host.Modules;
using Maran.Host.Modules.Enums;
using Maran.Sdk.Contracts;

namespace Maran.Host.Tests.Modules;

/// <summary>
/// Behavioral contract of the lifecycle-aware <see cref="ModuleStateResolver.Of(LicenceTier, ModuleLifecycleState, EntitlementVerdict)"/>:
/// a degraded or failed module is locked whatever its tier says, and every other state leaves the tier to decide.
/// </summary>
public sealed class ModuleStateResolverLifecycleTests
{
    /// <summary>A degraded or failed module is locked even when its tier asks for no licence.</summary>
    [Theory]
    [InlineData(ModuleLifecycleState.Degraded)]
    [InlineData(ModuleLifecycleState.Failed)]
    public void A_degraded_or_failed_module_is_locked_even_when_it_asks_for_no_licence(ModuleLifecycleState lifecycle)
    {
        Assert.Equal(
            ModuleState.Locked,
            ModuleStateResolver.Of(LicenceTier.Included, lifecycle, EntitlementVerdict.Covered));
    }

    /// <summary>An active, installed or removed state leaves the tier to decide.</summary>
    [Theory]
    [InlineData(ModuleLifecycleState.Active)]
    [InlineData(ModuleLifecycleState.Installed)]
    [InlineData(ModuleLifecycleState.Removed)]
    public void Any_other_state_leaves_the_tier_to_decide(ModuleLifecycleState lifecycle)
    {
        Assert.Equal(
            ModuleState.Enabled,
            ModuleStateResolver.Of(LicenceTier.Included, lifecycle, EntitlementVerdict.NotCovered));
        Assert.Equal(
            ModuleState.Locked,
            ModuleStateResolver.Of(LicenceTier.AddOn, lifecycle, EntitlementVerdict.NotCovered));
        Assert.Equal(
            ModuleState.Enabled,
            ModuleStateResolver.Of(LicenceTier.AddOn, lifecycle, EntitlementVerdict.Covered));
    }
}
