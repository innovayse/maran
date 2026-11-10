namespace Maran.Modules.Licensing.Domain.Enums;

/// <summary>
/// Where a licence's lease stands right now, as <see cref="Policies.LeaseGracePolicy"/> judges it. A
/// lease is issued for 14 days and renewed daily, so "the renewal is overdue but the lease still
/// holds" (<see cref="RenewalOverdue"/>) is a state of its own: the cover continues for the lease's
/// whole life, which is the grace the spec promises when the licence cloud cannot be reached.
/// Serialized as its camelCase member name.
/// </summary>
public enum LeaseStandingKind
{
    /// <summary>No lease is stored. For a licence that needs one this is a lapse: nothing grants cover on a missing lease.</summary>
    Missing,

    /// <summary>A lease is stored and could not be trusted (bad shape, bad signature, or no key in this build).</summary>
    Refused,

    /// <summary>The lease is valid and was issued within the last renewal interval.</summary>
    Fresh,

    /// <summary>The lease is valid but older than the renewal interval: the daily renewal has not arrived, the cover still holds.</summary>
    RenewalOverdue,

    /// <summary>The lease's validity has ended.</summary>
    Expired,

    /// <summary>The lease claims to be issued in the future beyond the skew bound: a forged "fresh" lease, or a badly wrong clock.</summary>
    FutureDated,

    /// <summary>The lease renews a different licence than the installed one.</summary>
    WrongLicence,

    /// <summary>The lease, or the licence it renews, is bound to another server than this one.</summary>
    WrongMachine,

    /// <summary>The licence is an offline licence, covered by its own expiry alone; no lease is asked for.</summary>
    NotRequired,
}
