using Maran.Modules.Licensing.Domain.Entities;
using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Domain.ValueObjects;
using Maran.Modules.Licensing.Models;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Licensing.Domain.Policies;

/// <summary>
/// The ONE rule that says whether the installed licence covers a module. The gate, the module
/// catalogue and a paid module's own jobs all reach it through the licence window, so "covered" cannot
/// mean two things (docs/superpowers/notes/2026-11-07-licensing-lease-threat-note.md §2, decision D5).
/// </summary>
/// <remarks>
/// <para>
/// <b>Until the manifest carries a required-tier field, an add-on and a plan-gated module are covered
/// by the same fact:</b> the licence's signed module list names the module. The licence's tier string
/// is display only (decision D5).
/// </para>
/// <para>
/// <b>A lease is mandatory unless the licence is offline</b> (decision D2): a valid, matching licence
/// whose lease is missing or ended has LAPSED; one whose lease is untrustworthy, future-dated or for
/// another licence or server covers nothing. Both refuse the same on the wire; the difference is only
/// what the administrator reads. Whether the module is <c>Included</c> is not asked here: an included
/// module never consults the licence at all.
/// </para>
/// </remarks>
public static class ModuleCoveragePolicy
{
    /// <summary>Decides one module's entitlement.</summary>
    /// <param name="coverage">The licence status and its lease standing.</param>
    /// <param name="moduleId">The module's <see cref="Manifest.Id"/>.</param>
    /// <returns>The module's entitlement.</returns>
    public static ModuleEntitlement VerdictOf(LicenceCoverage coverage, string moduleId)
    {
        return coverage.Status switch
        {
            LicenceStatus.Valid valid => VerdictForValid(valid.Licence, coverage.Lease, moduleId),
            LicenceStatus.Refused { LapsedLicence: { } lapsed } => Names(lapsed, moduleId)
                ? new ModuleEntitlement(moduleId, EntitlementVerdict.Lapsed, lapsed.Expiry)
                : NotCovered(moduleId),
            _ => NotCovered(moduleId),
        };
    }

    /// <summary>Decides a module's entitlement under a licence that verified.</summary>
    /// <param name="licence">The valid licence.</param>
    /// <param name="lease">The standing of its lease.</param>
    /// <param name="moduleId">The module's id.</param>
    /// <returns>The entitlement.</returns>
    private static ModuleEntitlement VerdictForValid(Licence licence, LeaseStanding lease, string moduleId)
    {
        if (!Names(licence, moduleId))
        {
            return NotCovered(moduleId);
        }

        if (licence.IsOffline)
        {
            return new ModuleEntitlement(moduleId, EntitlementVerdict.Covered, licence.Expiry);
        }

        return lease.Kind switch
        {
            LeaseStandingKind.Fresh or LeaseStandingKind.RenewalOverdue => new ModuleEntitlement(
                moduleId,
                EntitlementVerdict.Covered,
                Earlier(licence.Expiry, lease.ValidUntil)),
            LeaseStandingKind.Missing or LeaseStandingKind.Expired => new ModuleEntitlement(
                moduleId,
                EntitlementVerdict.Lapsed,
                lease.ValidUntil),
            _ => NotCovered(moduleId),
        };
    }

    /// <summary>Whether the licence's signed module list names the module.</summary>
    /// <param name="licence">The licence.</param>
    /// <param name="moduleId">The module's id.</param>
    /// <returns>True when named.</returns>
    private static bool Names(Licence licence, string moduleId)
    {
        return licence.Modules.Contains(moduleId, StringComparer.Ordinal);
    }

    /// <summary>The entitlement of a module nothing covers.</summary>
    /// <param name="moduleId">The module's id.</param>
    /// <returns>A not-covered entitlement with no end date.</returns>
    private static ModuleEntitlement NotCovered(string moduleId)
    {
        return new ModuleEntitlement(moduleId, EntitlementVerdict.NotCovered, null);
    }

    /// <summary>The earlier of a licence's expiry and a lease's end.</summary>
    /// <param name="expiry">The licence's expiry.</param>
    /// <param name="leaseEnd">The lease's end, or null.</param>
    /// <returns>The earlier instant.</returns>
    private static DateTimeOffset Earlier(DateTimeOffset expiry, DateTimeOffset? leaseEnd)
    {
        return leaseEnd is { } end && end < expiry ? end : expiry;
    }
}
