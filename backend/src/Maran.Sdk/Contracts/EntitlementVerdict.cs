namespace Maran.Sdk.Contracts;

/// <summary>
/// What the installed licence says about one module, as the panel's single licence window reports it
/// (<see cref="Interfaces.ILicenceEntitlements"/>). Three outcomes and no more: the gate, the module
/// catalogue and a paid module's own background work all branch on this value, so a fourth would have
/// to be understood by every one of them.
/// </summary>
public enum EntitlementVerdict
{
    /// <summary>The licence (and, where one is required, its lease) covers the module right now.</summary>
    Covered,

    /// <summary>Nothing installed covers the module: no licence, a refused one, or one that does not name it.</summary>
    NotCovered,

    /// <summary>
    /// The module WAS covered and its cover has ended: the licence expired, or its lease ran out or is
    /// missing. Distinct from <see cref="NotCovered"/> only in what the administrator is told; the gate
    /// refuses both the same way.
    /// </summary>
    Lapsed,
}
