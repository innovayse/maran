namespace Maran.Sdk.Contracts;

/// <summary>
/// The licence tier a module is sold under. Part of the module contract itself (spec §13): every
/// module — ours and third-party marketplace modules alike — declares its own tier here, so no
/// module needs to reference another module's identity to describe its licensing. Enforced
/// server-side on every request (rules/architecture.md "Where a module's UI lives") — the SPA only
/// hides what a tier does not unlock, it is never the boundary.
/// </summary>
public enum LicenceTier
{
    /// <summary>Bundled with every installation, regardless of plan. No separate licence check.</summary>
    Included,

    /// <summary>
    /// A separately-sold add-on module (e.g. mail, DNS, fleet management) that any panel plan may
    /// purchase individually, independent of which plan tier the installation is on.
    /// </summary>
    AddOn,

    /// <summary>
    /// Gated by the installation's panel plan rather than sold per module — available only once the
    /// account's plan includes it, with no separate add-on purchase.
    /// </summary>
    PlanGated,
}
