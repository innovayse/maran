using Maran.Modules.Dns.Domain.Policies;

namespace Maran.Modules.Dns.Tests.Domain.Policies;

/// <summary>The minimum wait before a withdrawal from signing may end, measured against an instant the caller supplies.</summary>
public sealed class DnssecWithdrawalPolicyTests
{
    /// <summary>When the withdrawal began.</summary>
    private static readonly DateTimeOffset Started = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    /// <summary>The wait is 48 hours.</summary>
    [Fact]
    public void The_minimum_wait_is_forty_eight_hours()
    {
        Assert.Equal(TimeSpan.FromHours(48), DnsLimits.DnssecWithdrawalMinimumWait);
        Assert.Equal(Started.AddHours(48), DnssecWithdrawalPolicy.EarliestFinish(Started));
    }

    /// <summary>An instant before the earliest finish is refused, down to the last tick.</summary>
    [Fact]
    public void An_instant_before_the_earliest_finish_is_refused()
    {
        Assert.False(DnssecWithdrawalPolicy.MayFinish(Started, Started));
        Assert.False(DnssecWithdrawalPolicy.MayFinish(Started, Started.AddHours(47)));
        Assert.False(DnssecWithdrawalPolicy.MayFinish(Started, Started.AddHours(48).AddTicks(-1)));
    }

    /// <summary>Inverse control: the earliest finish and any later instant are accepted.</summary>
    [Fact]
    public void The_earliest_finish_and_every_later_instant_are_accepted()
    {
        Assert.True(DnssecWithdrawalPolicy.MayFinish(Started, Started.AddHours(48)));
        Assert.True(DnssecWithdrawalPolicy.MayFinish(Started, Started.AddDays(30)));
    }

    /// <summary>A withdrawal whose row states no start is never allowed to end.</summary>
    [Fact]
    public void A_withdrawal_with_no_recorded_start_is_never_allowed_to_end()
    {
        Assert.False(DnssecWithdrawalPolicy.MayFinish(null, Started.AddYears(10)));
    }
}
