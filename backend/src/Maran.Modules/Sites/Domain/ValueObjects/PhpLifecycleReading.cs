using Maran.Modules.Sites.Domain.Enums;

namespace Maran.Modules.Sites.Domain.ValueObjects;

/// <summary>What the lifecycle table says about one PHP version on one day.</summary>
/// <param name="Status">Where the version stands: supported, security fixes only, end of life, or unknown.</param>
/// <param name="EndOfLife">
/// The last day the version receives security fixes, or <see langword="null"/> when the table has no
/// row for it. For an end-of-life version it is a date in the past, which is how the SPA can say since
/// when.
/// </param>
/// <param name="DaysLeft">
/// Whole days from the reading's day to <paramref name="EndOfLife"/> while the version still gets
/// security fixes (zero on the last day), or <see langword="null"/> when none are left or the version
/// is unknown. It counts to the end of security support rather than to the end of bug fixes because
/// that is the date after which running the version is a risk.
/// </param>
public sealed record PhpLifecycleReading(PhpSupportStatus Status, DateOnly? EndOfLife, int? DaysLeft);
