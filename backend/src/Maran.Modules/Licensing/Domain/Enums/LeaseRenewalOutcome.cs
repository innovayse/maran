namespace Maran.Modules.Licensing.Domain.Enums;

/// <summary>
/// What one pass of the lease renewal amounted to. A closed set: the worker, the journal and the alert
/// policy all branch on it, and none of them reads anything the service said except through it.
/// </summary>
public enum LeaseRenewalOutcome
{
    /// <summary>A new lease was fetched, verified and stored.</summary>
    Renewed,

    /// <summary>The stored lease is recent enough that nothing was asked of the service.</summary>
    NotDue,

    /// <summary>The licence is an offline licence: it needs no lease and the panel never calls the service for it.</summary>
    SkippedOffline,

    /// <summary>No licence is installed, or the installed one is not valid, so there is nothing to renew a lease for.</summary>
    SkippedNoLicence,

    /// <summary>
    /// The licence service has no usable address (not an https origin, or replaced by a client that makes no
    /// call, as in the public demonstration), so no request was made. Logged on every pass; not counted as a
    /// failure, because nothing was attempted.
    /// </summary>
    NotConfigured,

    /// <summary>The service could not be reached, timed out or answered with a server failure.</summary>
    Unreachable,

    /// <summary>The service answered that it would not issue a lease (an unknown or revoked licence, a rate limit).</summary>
    Refused,

    /// <summary>
    /// The service answered with something that is not a lease this licence can use: too large, not an
    /// envelope, signed by another key, for another licence or server, dated in the future or already over.
    /// Nothing was written.
    /// </summary>
    AnswerRejected,

    /// <summary>The service answered with a genuine lease that ends no later than the one already stored, so it was not written.</summary>
    NotNewer,

    /// <summary>A verified lease could not be written to the lease file.</summary>
    WriteFailed,

    /// <summary>The installed licence was replaced while the service was being asked, so its answer was dropped unwritten.</summary>
    LicenceChanged,
}
