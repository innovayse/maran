namespace Maran.Modules.Ssl.Domain.Enums;

/// <summary>
/// Where a certificate stands at a given instant, as the certificates list filters it. Every
/// certificate is in exactly one state: the three are exclusive and together cover every row, so a
/// filter on each of them in turn partitions the list.
/// </summary>
/// <remarks>
/// Not stored: it is a function of the row's own <c>NotAfter</c> and
/// <c>ConsecutiveRenewalFailures</c> and of the instant asked about, and a stored copy would go stale
/// the moment the clock passed an expiry. Whether a valid certificate is ALSO close to its expiry is a
/// separate question, answered by the list's <c>expiring</c> filter.
/// </remarks>
public enum CertificateStatus
{
    /// <summary>Not yet expired, and its last renewal attempt (if any) did not fail.</summary>
    Valid = 1,

    /// <summary>Not yet expired, but renewal has failed one or more times in a row since it last succeeded.</summary>
    RenewalFailing = 2,

    /// <summary>Its expiry has passed: browsers refuse it, whatever renewal has tried.</summary>
    Expired = 3,
}
