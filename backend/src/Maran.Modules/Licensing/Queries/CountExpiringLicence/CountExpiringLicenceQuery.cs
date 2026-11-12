namespace Maran.Modules.Licensing.Queries.CountExpiringLicence;

/// <summary>
/// Counts the installed licences that are within an expiry warning threshold: 1 while the one installed licence is
/// close to its expiry, 0 otherwise, for a screen that shows how many things need attention rather than which. A
/// panel holds at most one licence, so the count is the same one fact the licence screen warns about, asked as a
/// count the way every other attention source is.
/// </summary>
/// <remarks>
/// Dispatched by the panel itself, never bound from a request, so it carries no validator. The thresholds are the
/// backend's (<c>Licensing:ExpiryWarningDays</c>) and the instant is the panel's clock at the moment of the read.
/// </remarks>
public sealed record CountExpiringLicenceQuery;
