namespace Maran.Modules.Ssl.Queries.CountExpiringCertificates;

/// <summary>
/// Counts the certificates the caller may see whose expiry falls at or before an instant — those
/// already expired included — for a screen that shows how many need attention rather than which. A
/// count is asked as a count: the certificates list is paged, so a page of it holds one page and not
/// the total.
/// </summary>
/// <remarks>
/// Dispatched by the panel itself, never bound from a request, so it carries no validator.
/// </remarks>
/// <param name="Before">The instant at or before which a certificate's expiry counts it.</param>
public sealed record CountExpiringCertificatesQuery(DateTimeOffset Before);
