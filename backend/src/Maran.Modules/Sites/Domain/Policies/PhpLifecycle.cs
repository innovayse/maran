using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Domain.ValueObjects;

namespace Maran.Modules.Sites.Domain.Policies;

/// <summary>
/// The panel's own table of PHP support dates, and the rule that turns a version and a day into a
/// <see cref="PhpLifecycleReading"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Static data in the panel binary, by design.</b> There is no feed and no outbound call to learn
/// the dates (rules/security.md item 10); a new PHP release is a new row in a new panel release. A
/// version with no row reads <see cref="PhpSupportStatus.Unknown"/>, never as supported: the table
/// going stale makes the panel say less, not something false.
/// </para>
/// <para>
/// <b>Every date below is the author's recollection and is NOT verified.</b> The session that wrote
/// them had no network access, so each date line carries the marker comment
/// <c>TO BE VERIFIED against https://www.php.net/supported-versions.php</c>; the lead or a reviewer
/// with network access must check and correct them before a release relies on the dashboard figure.
/// </para>
/// <para>
/// <b>Both dates are inclusive.</b> A date is the last day of its phase: on the day itself a version
/// still counts as in that phase, and the next day it is in the following one.
/// </para>
/// </remarks>
public static class PhpLifecycle
{
    /// <summary>The support dates of every version the panel knows, by two-component version.</summary>
    private static readonly Dictionary<string, PhpLifecycleDates> Table =
        new Dictionary<string, PhpLifecycleDates>(StringComparer.Ordinal)
        {
            ["7.4"] = new PhpLifecycleDates(
                new DateOnly(2021, 11, 28), // TO BE VERIFIED against https://www.php.net/supported-versions.php (no network when written)
                new DateOnly(2022, 11, 28)), // TO BE VERIFIED against https://www.php.net/supported-versions.php (no network when written)
            ["8.0"] = new PhpLifecycleDates(
                new DateOnly(2022, 11, 26), // TO BE VERIFIED against https://www.php.net/supported-versions.php (no network when written)
                new DateOnly(2023, 11, 26)), // TO BE VERIFIED against https://www.php.net/supported-versions.php (no network when written)
            ["8.1"] = new PhpLifecycleDates(
                new DateOnly(2023, 11, 25), // TO BE VERIFIED against https://www.php.net/supported-versions.php (no network when written)
                new DateOnly(2025, 12, 31)), // TO BE VERIFIED against https://www.php.net/supported-versions.php (no network when written)
            ["8.2"] = new PhpLifecycleDates(
                new DateOnly(2024, 12, 31), // TO BE VERIFIED against https://www.php.net/supported-versions.php (no network when written)
                new DateOnly(2026, 12, 31)), // TO BE VERIFIED against https://www.php.net/supported-versions.php (no network when written)
            ["8.3"] = new PhpLifecycleDates(
                new DateOnly(2025, 12, 31), // TO BE VERIFIED against https://www.php.net/supported-versions.php (no network when written)
                new DateOnly(2027, 12, 31)), // TO BE VERIFIED against https://www.php.net/supported-versions.php (no network when written)
            ["8.4"] = new PhpLifecycleDates(
                new DateOnly(2026, 12, 31), // TO BE VERIFIED against https://www.php.net/supported-versions.php (no network when written)
                new DateOnly(2028, 12, 31)), // TO BE VERIFIED against https://www.php.net/supported-versions.php (no network when written)
        };

    /// <summary>Reads where one PHP version stands on one day.</summary>
    /// <param name="version">The two-component version as the packages name it, e.g. <c>8.3</c>.</param>
    /// <param name="today">The day to read it on, from the injected clock.</param>
    /// <returns>
    /// The status, the date security support ends and the days left until it; an unknown version reads
    /// <see cref="PhpSupportStatus.Unknown"/> with neither a date nor a count.
    /// </returns>
    public static PhpLifecycleReading Read(string version, DateOnly today)
    {
        if (!Table.TryGetValue(version, out var dates))
        {
            return new PhpLifecycleReading(PhpSupportStatus.Unknown, null, null);
        }

        if (today > dates.SecuritySupportEnds)
        {
            return new PhpLifecycleReading(PhpSupportStatus.EndOfLife, dates.SecuritySupportEnds, null);
        }

        var daysLeft = dates.SecuritySupportEnds.DayNumber - today.DayNumber;
        var status = today <= dates.ActiveSupportEnds ? PhpSupportStatus.Supported : PhpSupportStatus.SecurityOnly;

        return new PhpLifecycleReading(status, dates.SecuritySupportEnds, daysLeft);
    }
}
