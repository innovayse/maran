namespace Maran.Modules.Dns.Domain.Policies;

/// <summary>
/// When the end of a withdrawal from signing is allowed. The panel cannot see the registrar, so the wait is its
/// only protection against the one irreversible mistake a customer can make: ending signing while the DS record
/// is still published makes the zone fail validation for every validating resolver. The wait is measured against
/// the injected clock and applies to every caller, administrators included.
/// </summary>
public static class DnssecWithdrawalPolicy
{
    /// <summary>The instant before which a withdrawal that began at <paramref name="startedAt"/> may not end.</summary>
    /// <param name="startedAt">When the withdrawal began.</param>
    /// <returns>The earliest instant the end is allowed.</returns>
    public static DateTimeOffset EarliestFinish(DateTimeOffset startedAt)
    {
        return startedAt + DnsLimits.DnssecWithdrawalMinimumWait;
    }

    /// <summary>Tells whether a withdrawal may end at <paramref name="now"/>.</summary>
    /// <param name="startedAt">When the withdrawal began, or null when the row does not say, which is never allowed.</param>
    /// <param name="now">The instant, from the clock.</param>
    /// <returns>True from the earliest finish onward, false before it and false for a withdrawal with no start.</returns>
    public static bool MayFinish(DateTimeOffset? startedAt, DateTimeOffset now)
    {
        return startedAt is { } started && now >= EarliestFinish(started);
    }
}
