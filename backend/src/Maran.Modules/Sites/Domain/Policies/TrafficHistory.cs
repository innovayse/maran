namespace Maran.Modules.Sites.Domain.Policies;

/// <summary>What the panel keeps of a site's traffic, and the windows a customer may read it over.</summary>
public static class TrafficHistory
{
    /// <summary>How many days of per-day rows are kept before the nightly purge removes them.</summary>
    public const int KeptDays = 90;

    /// <summary>
    /// The most distinct paths one day row keeps. Beyond it a path's requests are counted under the
    /// day's "other" figure, so the totals stay true while the stored row stays bounded.
    /// </summary>
    public const int MaximumPaths = 50;

    /// <summary>The lowest HTTP status code a row records; a code outside the range is not a status.</summary>
    public const int FirstStatusCode = 100;

    /// <summary>The highest HTTP status code a row records.</summary>
    public const int LastStatusCode = 599;

    /// <summary>The windows, in days, a customer may read the summary over; the first is the one shown first.</summary>
    public static readonly IReadOnlyList<int> Windows = [7, 30, KeptDays];

    /// <summary>Whether <paramref name="days"/> is one of the windows a customer may read.</summary>
    /// <param name="days">The candidate window.</param>
    /// <returns><c>true</c> when it is listed in <see cref="Windows"/>.</returns>
    public static bool IsWindow(int days)
    {
        return Windows.Contains(days);
    }
}
