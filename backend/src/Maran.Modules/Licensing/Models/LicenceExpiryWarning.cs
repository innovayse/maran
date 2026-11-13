namespace Maran.Modules.Licensing.Models;

/// <summary>
/// The fact that the installed licence is close to its expiry: which of the configured thresholds it has crossed
/// and how long is left. Exists only for a licence that is still in date; a licence that has expired is a refusal
/// of its own, not a warning.
/// </summary>
/// <param name="ThresholdDays">
/// The smallest configured threshold, in days, that the remaining time is within. It changes exactly when a
/// further threshold is crossed, so it is what decides whether a notification is due again.
/// </param>
/// <param name="DaysLeft">The remaining time in whole days, rounded up, so a licence with an hour left reads as 1.</param>
/// <param name="Expiry">The instant the licence expires.</param>
public sealed record LicenceExpiryWarning(int ThresholdDays, int DaysLeft, DateTimeOffset Expiry);
