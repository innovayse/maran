namespace Maran.Modules.Licensing.Models;

/// <summary>
/// What the operator was last told about the licence's approaching expiry: the licence (by its expiry instant) and the
/// threshold that was announced. Two announcements are the same fact when both parts are equal, so a licence announced
/// at 14 days is announced again only at 3 days, and a replacement licence with another expiry is announced afresh.
/// </summary>
/// <param name="Expiry">The expiry instant of the licence that was announced.</param>
/// <param name="ThresholdDays">The threshold, in days, that was announced.</param>
public sealed record LicenceExpiryAnnouncement(DateTimeOffset Expiry, int ThresholdDays);
