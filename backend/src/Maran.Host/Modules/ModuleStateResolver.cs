using Maran.Host.Modules.Enums;
using Maran.Sdk.Contracts;

namespace Maran.Host.Modules;

/// <summary>
/// The one place that decides whether a composed module is <see cref="ModuleState.Enabled"/> or
/// <see cref="ModuleState.Locked"/>. <see cref="ModulesEndpoint"/> asks it for every module and
/// nothing else computes a module's state, so a licence check that arrives later changes this
/// file and not the contract.
/// </summary>
public static class ModuleStateResolver
{
    /// <summary>
    /// Resolves the state of a module from the tier its manifest declares.
    /// </summary>
    /// <remarks>
    /// A module whose tier is <see cref="LicenceTier.Included"/> asks for no licence and is enabled.
    /// A module sold as <see cref="LicenceTier.AddOn"/> or <see cref="LicenceTier.PlanGated"/> is
    /// locked: no read-only window onto an installed licence exists in the Sdk yet (the Licensing
    /// module is not composed into the registry, and a module may not reference it), so nothing can
    /// yet say such a module is covered, and failing closed is the only answer that does not claim a
    /// licence nobody checked. Every module the panel ships declares <c>Included</c>, so today
    /// <see cref="ModuleState.Locked"/> is produced only by a marketplace module that declares a
    /// paid tier; when the licence window lands, this method gains its parameter and the paid arms
    /// read it.
    /// </remarks>
    /// <param name="tier">The tier the module declares in its manifest.</param>
    /// <returns>The module's state.</returns>
    public static ModuleState Of(LicenceTier tier)
    {
        return tier switch
        {
            LicenceTier.Included => ModuleState.Enabled,
            LicenceTier.AddOn => ModuleState.Locked,
            LicenceTier.PlanGated => ModuleState.Locked,
            _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown licence tier."),
        };
    }

    /// <summary>
    /// Resolves the state of a module from its tier and from the state the runtime registry reports for
    /// it. A module that is <see cref="ModuleLifecycleState.Degraded"/> (its lease lapsed) or
    /// <see cref="ModuleLifecycleState.Failed"/> is not usable whatever its tier says, so it is
    /// <see cref="ModuleState.Locked"/>; every other lifecycle state leaves the tier to decide.
    /// </summary>
    /// <remarks>
    /// Presentation only, like the tier arm: a degraded module answers the licence response on every
    /// request regardless of what the catalogue shows, and a lapse never makes a module in the Core kind
    /// anything but active (the registry never reports one as degraded).
    /// </remarks>
    /// <param name="tier">The tier the module declares in its manifest.</param>
    /// <param name="lifecycle">The state the runtime registry reports for it.</param>
    /// <returns>The module's state.</returns>
    public static ModuleState Of(LicenceTier tier, ModuleLifecycleState lifecycle)
    {
        return lifecycle is ModuleLifecycleState.Degraded or ModuleLifecycleState.Failed
            ? ModuleState.Locked
            : Of(tier);
    }
}
