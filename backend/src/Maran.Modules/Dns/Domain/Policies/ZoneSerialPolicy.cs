namespace Maran.Modules.Dns.Domain.Policies;

/// <summary>
/// How a zone's SOA serial moves: <c>YYYYMMDDnn</c>, monotonic. It is computed here, sent to the agent and
/// never derived by the agent, so the panel's row and the daemon's zone cannot disagree about it.
/// </summary>
public static class ZoneSerialPolicy
{
    /// <summary>The factor that puts a date in front of the two-digit change counter.</summary>
    private const long DateShift = 100;

    /// <summary>The serial that follows <paramref name="current"/> on <paramref name="now"/>.</summary>
    /// <param name="current">The zone's present serial, or zero for a zone that has none yet.</param>
    /// <param name="now">The change instant, from the clock.</param>
    /// <returns>
    /// The larger of today's first serial and <c>current + 1</c>. A day with more than a hundred changes runs
    /// past <c>99</c> into the next day's range, which is still increasing, and that is all a serial must be.
    /// </returns>
    public static long Next(long current, DateTimeOffset now)
    {
        var today = (now.Year * 10_000L) + (now.Month * 100L) + now.Day;

        return Math.Max(current + 1, today * DateShift);
    }
}
