using Maran.Modules.Licensing.Domain.Entities;
using Maran.Modules.Licensing.Domain.Policies;
using Maran.Modules.Licensing.Domain.ValueObjects;
using Maran.Modules.Licensing.Tests.TestSupport;

namespace Maran.Modules.Licensing.Tests.Domain.Policies;

/// <summary>
/// Covers <see cref="LicenceExpiryWarningPolicy"/>: where each threshold begins, exactly at its edge, one second
/// either side of it, an expired licence and the configuration rules. Every refusing case has an accepting control
/// beside it, so a rule of "never warn" or "always warn" fails one of them.
/// </summary>
public sealed class LicenceExpiryWarningPolicyTests
{
    /// <summary>The instant the licence is judged at.</summary>
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The thresholds the product ships with, smallest first.</summary>
    private static readonly IReadOnlyList<int> Defaults = LicenceExpiryWarningPolicy.Resolve(null);

    /// <summary>Builds a licence expiring at an instant.</summary>
    /// <param name="expiry">The expiry.</param>
    /// <returns>The licence.</returns>
    private static Licence ExpiringAt(DateTimeOffset expiry)
    {
        return new Licence(LicenceId.Of("lic-001"), "maran", "included", ["mail"], expiry);
    }

    /// <summary>The defaults are 30, 14 and 3 days, smallest first.</summary>
    [Fact]
    public void The_defaults_are_thirty_fourteen_and_three_days()
    {
        Assert.Equal([3, 14, 30], Defaults);
    }

    /// <summary>Exactly on a threshold's edge the licence is inside it; this is the accepting control for the case below.</summary>
    [Theory]
    [InlineData(30, 30)]
    [InlineData(14, 14)]
    [InlineData(3, 3)]
    public void A_licence_exactly_at_a_threshold_is_inside_it(int days, int expectedThreshold)
    {
        var warning = LicenceExpiryWarningPolicy.Evaluate(
            ExpiringAt(Now.AddDays(days)), new FixedClock(Now), Defaults);

        Assert.NotNull(warning);
        Assert.Equal(expectedThreshold, warning.ThresholdDays);
        Assert.Equal(days, warning.DaysLeft);
    }

    /// <summary>One second beyond the 30-day edge there is no warning; one second inside the 14- and 3-day edges the threshold stays the wider one.</summary>
    [Fact]
    public void A_licence_one_second_outside_the_widest_threshold_has_no_warning()
    {
        var outside = LicenceExpiryWarningPolicy.Evaluate(
            ExpiringAt(Now.AddDays(30).AddSeconds(1)), new FixedClock(Now), Defaults);
        var inside = LicenceExpiryWarningPolicy.Evaluate(
            ExpiringAt(Now.AddDays(30).AddSeconds(-1)), new FixedClock(Now), Defaults);

        Assert.Null(outside);
        Assert.NotNull(inside);
    }

    /// <summary>Crossing the 14-day and 3-day edges moves the licence to the narrower threshold, one second at a time.</summary>
    [Theory]
    [InlineData(14, 30, 14)]
    [InlineData(3, 14, 3)]
    public void A_licence_one_second_outside_a_narrower_threshold_is_still_in_the_wider_one(
        int edgeDays,
        int outsideThreshold,
        int insideThreshold)
    {
        var outside = LicenceExpiryWarningPolicy.Evaluate(
            ExpiringAt(Now.AddDays(edgeDays).AddSeconds(1)), new FixedClock(Now), Defaults);
        var inside = LicenceExpiryWarningPolicy.Evaluate(
            ExpiringAt(Now.AddDays(edgeDays).AddSeconds(-1)), new FixedClock(Now), Defaults);

        Assert.Equal(outsideThreshold, outside?.ThresholdDays);
        Assert.Equal(insideThreshold, inside?.ThresholdDays);
    }

    /// <summary>The days left are rounded up, so a licence with an hour left reads as one day, never zero.</summary>
    [Fact]
    public void The_days_left_are_rounded_up()
    {
        var warning = LicenceExpiryWarningPolicy.Evaluate(
            ExpiringAt(Now.AddHours(1)), new FixedClock(Now), Defaults);

        Assert.Equal(1, warning?.DaysLeft);
        Assert.Equal(3, warning?.ThresholdDays);
    }

    /// <summary>A licence that has expired, or expires this very instant, has no warning; the one an hour before it does.</summary>
    [Fact]
    public void An_expired_licence_has_no_warning_and_the_one_just_before_it_does()
    {
        Assert.Null(LicenceExpiryWarningPolicy.Evaluate(ExpiringAt(Now), new FixedClock(Now), Defaults));
        Assert.Null(LicenceExpiryWarningPolicy.Evaluate(ExpiringAt(Now.AddDays(-1)), new FixedClock(Now), Defaults));
        Assert.NotNull(LicenceExpiryWarningPolicy.Evaluate(ExpiringAt(Now.AddSeconds(1)), new FixedClock(Now), Defaults));
    }

    /// <summary>Configured thresholds replace the defaults: a licence 20 days out warns with 21 configured and not with the defaults narrowed to 7.</summary>
    [Fact]
    public void Configured_thresholds_replace_the_defaults()
    {
        var licence = ExpiringAt(Now.AddDays(20));

        Assert.Equal(21, LicenceExpiryWarningPolicy.Evaluate(licence, new FixedClock(Now), LicenceExpiryWarningPolicy.Resolve([21, 7]))?.ThresholdDays);
        Assert.Null(LicenceExpiryWarningPolicy.Evaluate(licence, new FixedClock(Now), LicenceExpiryWarningPolicy.Resolve([7])));
    }

    /// <summary>The configured list is sorted and de-duplicated.</summary>
    [Fact]
    public void The_configured_list_is_sorted_and_without_repeats()
    {
        Assert.Equal([2, 9], LicenceExpiryWarningPolicy.Resolve([9, 2, 9]));
    }

    /// <summary>
    /// An empty list turns the warning off, whatever the licence: it resolves to no threshold and a licence one day
    /// from its expiry is not within one. Null, which is nothing configured, is the inverse control: the defaults,
    /// under which the same licence warns.
    /// </summary>
    [Fact]
    public void An_empty_list_means_no_warning_and_null_means_the_defaults()
    {
        var oneDayLeft = ExpiringAt(Now.AddDays(1));

        Assert.Empty(LicenceExpiryWarningPolicy.Resolve([]));
        Assert.Null(LicenceExpiryWarningPolicy.Evaluate(oneDayLeft, new FixedClock(Now), LicenceExpiryWarningPolicy.Resolve([])));
        Assert.Equal(3, LicenceExpiryWarningPolicy.Evaluate(oneDayLeft, new FixedClock(Now), LicenceExpiryWarningPolicy.Resolve(null))?.ThresholdDays);
        Assert.True(LicenceExpiryWarningPolicy.AreValid([]));
    }

    /// <summary>A value outside 1 to 365 is invalid; null and in-range values are the accepting controls.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(366)]
    public void A_threshold_outside_one_to_three_hundred_sixty_five_is_invalid(int days)
    {
        Assert.False(LicenceExpiryWarningPolicy.AreValid([30, days]));
    }

    /// <summary>The accepting control for the case above.</summary>
    [Fact]
    public void Unset_and_in_range_thresholds_are_valid()
    {
        Assert.True(LicenceExpiryWarningPolicy.AreValid(null));
        Assert.True(LicenceExpiryWarningPolicy.AreValid([1, 365]));
    }
}
