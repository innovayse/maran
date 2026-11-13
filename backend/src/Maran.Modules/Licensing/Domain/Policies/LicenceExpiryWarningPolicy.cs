using Maran.Modules.Licensing.Domain.Entities;
using Maran.Modules.Licensing.Models;

namespace Maran.Modules.Licensing.Domain.Policies;

/// <summary>
/// The ONE rule of when an installed licence is "close to expiring": which thresholds exist, and which of them
/// a licence has crossed at a given instant. Pure over its arguments, so the boundary of every threshold has a
/// unit test and nothing that shows the warning (the licence screen, <c>maran status</c>, the notification)
/// holds a judgement of its own.
/// </summary>
/// <remarks>
/// <para>
/// <b>A threshold is crossed when the time left is within it, boundary included.</b> A licence with exactly 30
/// days left has crossed the 30-day threshold; one with 30 days and a second left has not. The expiry itself is
/// still decided by <see cref="LicenceExpiryPolicy"/>: a licence that is expired gets no warning, because the
/// refusal that follows already says so.
/// </para>
/// <para>
/// <b>An empty list means warnings off.</b> Null (nothing configured) is the defaults; a list with no thresholds is
/// the operator saying there are none, so no licence is ever within one and nothing is warned about. The two are
/// different answers and are never conflated, or an operator could neither turn the warning off nor tell why an
/// empty list did nothing.
/// </para>
/// <para>
/// <b>The warning is for a licence that was verified.</b> Whether a licence is installed, accepted or bound to
/// this server is not this policy's business; it is given a <see cref="Licence"/> and judges its end date only.
/// </para>
/// </remarks>
public static class LicenceExpiryWarningPolicy
{
    /// <summary>The largest threshold that may be configured, in days.</summary>
    public const int MaxThresholdDays = 365;

    /// <summary>The thresholds in effect when none are configured: a month, two weeks and three days before the expiry.</summary>
    public static readonly IReadOnlyList<int> DefaultThresholdDays = [30, 14, 3];

    /// <summary>Whether a configured list of thresholds is acceptable.</summary>
    /// <param name="configured">The configured thresholds, null for the defaults, or empty for no warning at all.</param>
    /// <returns>
    /// <c>true</c> when it is null, empty (warnings off, a deliberate choice) or every value is between 1 and
    /// <see cref="MaxThresholdDays"/>.
    /// </returns>
    public static bool AreValid(IReadOnlyList<int>? configured)
    {
        return configured is null || configured.All(days => { return days >= 1 && days <= MaxThresholdDays; });
    }

    /// <summary>Turns the configured thresholds into the list the evaluation uses.</summary>
    /// <param name="configured">The configured thresholds, null for the defaults, or empty for no warning at all.</param>
    /// <returns>
    /// The thresholds, each once, smallest first; the defaults when nothing is configured (null); none when the list
    /// is empty, which is how an operator turns the warning off.
    /// </returns>
    public static IReadOnlyList<int> Resolve(IReadOnlyList<int>? configured)
    {
        if (configured is null)
        {
            return DefaultThresholdDays.OrderBy(days => { return days; }).ToList();
        }

        return configured.Distinct().OrderBy(days => { return days; }).ToList();
    }

    /// <summary>Judges whether a licence is within a warning threshold now.</summary>
    /// <param name="licence">The installed, verified licence.</param>
    /// <param name="clock">The panel's injected time source.</param>
    /// <param name="thresholdDays">The thresholds from <see cref="Resolve"/>, smallest first.</param>
    /// <returns>
    /// The warning, or null when the licence has expired or is further from its expiry than the largest threshold.
    /// </returns>
    public static LicenceExpiryWarning? Evaluate(Licence licence, IClock clock, IReadOnlyList<int> thresholdDays)
    {
        if (LicenceExpiryPolicy.IsExpired(licence, clock))
        {
            return null;
        }

        var remaining = licence.Expiry - clock.UtcNow;

        foreach (var days in thresholdDays)
        {
            if (remaining <= TimeSpan.FromDays(days))
            {
                return new LicenceExpiryWarning(days, (int)Math.Ceiling(remaining.TotalDays), licence.Expiry);
            }
        }

        return null;
    }
}
