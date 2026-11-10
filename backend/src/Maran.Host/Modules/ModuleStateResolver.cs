using Maran.Host.Modules.Enums;
using Maran.Sdk.Contracts;

namespace Maran.Host.Modules;

/// <summary>
/// The one place that decides whether a composed module is <see cref="ModuleState.Enabled"/>,
/// <see cref="ModuleState.Lapsed"/> or <see cref="ModuleState.Locked"/>. <see cref="ModulesEndpoint"/> asks
/// it for every module and nothing else computes a module's state. The verdict it reads comes from the
/// same licence window the request-time gate asks (<see cref="EntitlementVerdict"/>), so the menu and
/// the boundary cannot disagree about what "locked" means: the menu is advice, the gate is the boundary.
/// </summary>
public static class ModuleStateResolver
{
    /// <summary>Resolves the state of a module from its tier and the licence window's verdict for it.</summary>
    /// <remarks>
    /// A module whose tier is <see cref="LicenceTier.Included"/> asks for no licence and is enabled
    /// whatever the verdict says. A module sold as <see cref="LicenceTier.AddOn"/> or
    /// <see cref="LicenceTier.PlanGated"/> follows the verdict: covered is enabled, lapsed (it was
    /// covered and the cover ended) is <see cref="ModuleState.Lapsed"/>, anything else is locked.
    /// </remarks>
    /// <param name="tier">The tier the module declares in its manifest.</param>
    /// <param name="verdict">What the licence window says about the module.</param>
    /// <returns>The module's state.</returns>
    public static ModuleState Of(LicenceTier tier, EntitlementVerdict verdict)
    {
        return tier switch
        {
            LicenceTier.Included => ModuleState.Enabled,
            LicenceTier.AddOn => OfPaid(verdict),
            LicenceTier.PlanGated => OfPaid(verdict),
            _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown licence tier."),
        };
    }

    /// <summary>
    /// Resolves the state of a module from its tier, the licence window's verdict, and the state the runtime
    /// registry reports for it. A module that is <see cref="ModuleLifecycleState.Degraded"/> (its lease
    /// lapsed) or <see cref="ModuleLifecycleState.Failed"/> is not usable whatever its tier says, so it
    /// is <see cref="ModuleState.Locked"/>; every other lifecycle state leaves the tier and the verdict
    /// to decide.
    /// </summary>
    /// <remarks>
    /// Presentation only, like the tier arm: a degraded module answers the licence response on every
    /// request regardless of what the catalogue shows, and a lapse never makes a module in the Core kind
    /// anything but active (the registry never reports one as degraded).
    /// </remarks>
    /// <param name="tier">The tier the module declares in its manifest.</param>
    /// <param name="lifecycle">The state the runtime registry reports for it.</param>
    /// <param name="verdict">What the licence window says about the module.</param>
    /// <returns>The module's state.</returns>
    public static ModuleState Of(LicenceTier tier, ModuleLifecycleState lifecycle, EntitlementVerdict verdict)
    {
        return lifecycle is ModuleLifecycleState.Degraded or ModuleLifecycleState.Failed
            ? ModuleState.Locked
            : Of(tier, verdict);
    }

    /// <summary>The state of a paid module for a verdict.</summary>
    /// <param name="verdict">What the licence window says about the module.</param>
    /// <returns>Enabled, lapsed or locked.</returns>
    private static ModuleState OfPaid(EntitlementVerdict verdict)
    {
        return verdict switch
        {
            EntitlementVerdict.Covered => ModuleState.Enabled,
            EntitlementVerdict.Lapsed => ModuleState.Lapsed,
            EntitlementVerdict.NotCovered => ModuleState.Locked,
            _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "Unknown entitlement verdict."),
        };
    }
}
