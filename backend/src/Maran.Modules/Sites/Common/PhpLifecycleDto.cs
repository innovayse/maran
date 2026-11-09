using Maran.Modules.Sites.Domain.Enums;

namespace Maran.Modules.Sites.Common;

/// <summary>
/// Outward view of where one PHP version stands in its upstream support life, read from the panel's
/// own table and never from a feed.
/// </summary>
/// <param name="Status">Supported, security fixes only, end of life, or unknown. Unknown is never rendered as supported.</param>
/// <param name="StatusName">The status as the reader's language names it; the backend's to produce, shown as received.</param>
/// <param name="EndOfLife">
/// The last day the version receives security fixes, or <see langword="null"/> when the panel has no
/// row for the version. A date in the past for an end-of-life version.
/// </param>
/// <param name="DaysLeft">
/// Whole days until <paramref name="EndOfLife"/> while security fixes continue, or <see langword="null"/>
/// when none are left or the version is unknown.
/// </param>
public sealed record PhpLifecycleDto(PhpSupportStatus Status, string StatusName, DateOnly? EndOfLife, int? DaysLeft);
