using Maran.Modules.Licensing.Domain.Entities;
using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Domain.Policies;
using Maran.Modules.Licensing.Domain.ValueObjects;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Licensing.Tests.Domain.Policies;

/// <summary>
/// Covers <see cref="LeaseRenewalPolicy"/>: when a renewal is due, when an answer improves on the stored lease,
/// which outcomes are failures, how long to wait, and when the operator is told. Every refusing case sits beside
/// the accepting control that proves the rule is not "refuse everything".
/// </summary>
public sealed class LeaseRenewalPolicyTests
{
    /// <summary>The moment the policy is asked at.</summary>
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The installed licence.</summary>
    private static readonly Licence Installed = new(LicenceId.Of("lic-001"), "maran", "included", ["mail"], Now.AddYears(1));

    /// <summary>A stored lease issued <paramref name="age"/> ago for <paramref name="licence"/>, lasting fourteen days.</summary>
    /// <param name="age">How long ago it was issued.</param>
    /// <param name="licence">The licence it names.</param>
    /// <returns>The reading the file source would give.</returns>
    private static LeaseReading.Present Stored(TimeSpan age, string licence = "lic-001")
    {
        return new LeaseReading.Present(new LicenceLease(licence, Now - age, Now - age + TimeSpan.FromDays(14), null));
    }

    /// <summary>A lease that is due for no reason but its age is not renewed: the control for every due case below.</summary>
    [Fact]
    public void A_lease_issued_an_hour_ago_for_this_licence_is_not_due()
    {
        Assert.False(LeaseRenewalPolicy.IsDue(Stored(TimeSpan.FromHours(1)), Installed, Now));
    }

    /// <summary>The age threshold is inclusive at twenty hours and exclusive just under it.</summary>
    [Fact]
    public void A_lease_is_due_from_exactly_twenty_hours_of_age_and_not_a_second_before()
    {
        Assert.False(LeaseRenewalPolicy.IsDue(Stored(LeaseRenewalPolicy.MinimumLeaseAge - TimeSpan.FromSeconds(1)), Installed, Now));
        Assert.True(LeaseRenewalPolicy.IsDue(Stored(LeaseRenewalPolicy.MinimumLeaseAge), Installed, Now));
    }

    /// <summary>No lease, a refused lease, and a lease for another licence all need a renewal at once.</summary>
    [Fact]
    public void A_missing_refused_or_foreign_lease_is_due_at_once()
    {
        Assert.True(LeaseRenewalPolicy.IsDue(LeaseReading.Absent.Instance, Installed, Now));
        Assert.True(LeaseRenewalPolicy.IsDue(new LeaseReading.Refused(LeaseRefusalReason.SignatureInvalid), Installed, Now));
        Assert.True(LeaseRenewalPolicy.IsDue(Stored(TimeSpan.FromHours(1), licence: "lic-999"), Installed, Now));
    }

    /// <summary>A lease dated beyond the skew bound is replaced; one inside the bound is left alone.</summary>
    [Fact]
    public void A_future_dated_lease_is_due_but_one_inside_the_skew_bound_is_not()
    {
        var beyond = new LeaseReading.Present(new LicenceLease("lic-001", Now + TimeSpan.FromHours(1), Now.AddDays(15), null));
        var inside = new LeaseReading.Present(new LicenceLease("lic-001", Now + TimeSpan.FromMinutes(1), Now.AddDays(15), null));

        Assert.True(LeaseRenewalPolicy.IsDue(beyond, Installed, Now));
        Assert.False(LeaseRenewalPolicy.IsDue(inside, Installed, Now));
    }

    /// <summary>An answer that ends later than a standing lease improves on it; one that ends sooner or at the same moment does not.</summary>
    [Fact]
    public void An_answer_improves_on_a_standing_lease_only_by_ending_later()
    {
        var standing = new LeaseStanding(LeaseStandingKind.Fresh, Now.AddDays(10));

        Assert.True(LeaseRenewalPolicy.Improves(standing, new LicenceLease("lic-001", Now, Now.AddDays(10).AddSeconds(1), null)));
        Assert.False(LeaseRenewalPolicy.Improves(standing, new LicenceLease("lic-001", Now, Now.AddDays(10), null)));
        Assert.False(LeaseRenewalPolicy.Improves(standing, new LicenceLease("lic-001", Now, Now.AddDays(9), null)));
        Assert.True(LeaseRenewalPolicy.Improves(
            new LeaseStanding(LeaseStandingKind.RenewalOverdue, Now.AddDays(10)),
            new LicenceLease("lic-001", Now, Now.AddDays(11), null)));
    }

    /// <summary>A stored lease that does not stand as good never blocks a replacement, however far its dates reach.</summary>
    [Theory]
    [InlineData(LeaseStandingKind.Missing)]
    [InlineData(LeaseStandingKind.Refused)]
    [InlineData(LeaseStandingKind.Expired)]
    [InlineData(LeaseStandingKind.FutureDated)]
    [InlineData(LeaseStandingKind.WrongLicence)]
    [InlineData(LeaseStandingKind.WrongMachine)]
    [InlineData(LeaseStandingKind.NotRequired)]
    public void A_stored_lease_that_does_not_stand_is_always_replaced(LeaseStandingKind kind)
    {
        var standing = new LeaseStanding(kind, Now.AddYears(5));

        Assert.True(LeaseRenewalPolicy.Improves(standing, new LicenceLease("lic-001", Now, Now.AddDays(1), null)));
    }

    /// <summary>Every outcome that means "a renewal was due and none was stored" is a failure; the rest are not.</summary>
    [Theory]
    [InlineData(LeaseRenewalOutcome.Unreachable, true)]
    [InlineData(LeaseRenewalOutcome.Refused, true)]
    [InlineData(LeaseRenewalOutcome.AnswerRejected, true)]
    [InlineData(LeaseRenewalOutcome.NotNewer, true)]
    [InlineData(LeaseRenewalOutcome.WriteFailed, true)]
    [InlineData(LeaseRenewalOutcome.Renewed, false)]
    [InlineData(LeaseRenewalOutcome.NotDue, false)]
    [InlineData(LeaseRenewalOutcome.SkippedOffline, false)]
    [InlineData(LeaseRenewalOutcome.SkippedNoLicence, false)]
    [InlineData(LeaseRenewalOutcome.NotConfigured, false)]
    [InlineData(LeaseRenewalOutcome.LicenceChanged, false)]
    public void Only_a_due_renewal_that_stored_nothing_is_a_failure(LeaseRenewalOutcome outcome, bool expected)
    {
        Assert.Equal(expected, LeaseRenewalPolicy.IsFailure(outcome));
    }

    /// <summary>Every outcome has a wait, none is shorter than its base, and each stays inside its jitter span.</summary>
    [Theory]
    [InlineData(LeaseRenewalOutcome.Renewed, 22, 24)]
    [InlineData(LeaseRenewalOutcome.Unreachable, 6, 7)]
    [InlineData(LeaseRenewalOutcome.Refused, 6, 7)]
    [InlineData(LeaseRenewalOutcome.AnswerRejected, 6, 7)]
    [InlineData(LeaseRenewalOutcome.NotNewer, 6, 7)]
    [InlineData(LeaseRenewalOutcome.WriteFailed, 6, 7)]
    public void The_wait_after_a_pass_is_its_base_plus_a_share_of_its_jitter(
        LeaseRenewalOutcome outcome,
        int baseHours,
        int ceilingHours)
    {
        Assert.Equal(TimeSpan.FromHours(baseHours), LeaseRenewalPolicy.DelayAfter(outcome, 0.0));
        Assert.Equal(TimeSpan.FromHours(ceilingHours), LeaseRenewalPolicy.DelayAfter(outcome, 1.0));
        Assert.True(LeaseRenewalPolicy.DelayAfter(outcome, 0.5) > LeaseRenewalPolicy.DelayAfter(outcome, 0.0));
    }

    /// <summary>A pass that had nothing to do is repeated after about an hour, and a pass that found a recent lease after about two.</summary>
    [Fact]
    public void A_pass_with_nothing_to_do_waits_about_an_hour_and_a_recent_lease_about_two()
    {
        Assert.Equal(TimeSpan.FromHours(1), LeaseRenewalPolicy.DelayAfter(LeaseRenewalOutcome.SkippedNoLicence, 0.0));
        Assert.Equal(TimeSpan.FromHours(1), LeaseRenewalPolicy.DelayAfter(LeaseRenewalOutcome.SkippedOffline, 0.0));
        Assert.Equal(TimeSpan.FromHours(1), LeaseRenewalPolicy.DelayAfter(LeaseRenewalOutcome.NotConfigured, 0.0));
        Assert.Equal(TimeSpan.FromHours(1), LeaseRenewalPolicy.DelayAfter(LeaseRenewalOutcome.LicenceChanged, 0.0));
        Assert.Equal(TimeSpan.FromHours(2), LeaseRenewalPolicy.DelayAfter(LeaseRenewalOutcome.NotDue, 0.0));
        Assert.True(LeaseRenewalPolicy.DelayAfter(LeaseRenewalOutcome.NotDue, 1.0) < TimeSpan.FromHours(3));
    }

    /// <summary>A random fraction outside [0, 1] cannot lengthen or shorten a wait beyond its span.</summary>
    [Fact]
    public void A_jitter_outside_the_unit_interval_is_clamped()
    {
        Assert.Equal(
            LeaseRenewalPolicy.DelayAfter(LeaseRenewalOutcome.Renewed, 0.0),
            LeaseRenewalPolicy.DelayAfter(LeaseRenewalOutcome.Renewed, -3.0));
        Assert.Equal(
            LeaseRenewalPolicy.DelayAfter(LeaseRenewalOutcome.Renewed, 1.0),
            LeaseRenewalPolicy.DelayAfter(LeaseRenewalOutcome.Renewed, 7.0));
    }

    /// <summary>The success wait always ends before the lease would be called overdue, so a healthy panel never shows overdue.</summary>
    [Fact]
    public void The_longest_success_wait_is_not_longer_than_the_renewal_interval()
    {
        Assert.True(LeaseRenewalPolicy.DelayAfter(LeaseRenewalOutcome.Renewed, 1.0) <= LeaseGracePolicy.RenewalInterval);
    }

    /// <summary>The first pass waits at least two minutes and at most ten, so it never competes with start-up.</summary>
    [Fact]
    public void The_first_pass_waits_between_two_and_ten_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), LeaseRenewalPolicy.DelayBeforeFirstPass(0.0));
        Assert.Equal(TimeSpan.FromMinutes(10), LeaseRenewalPolicy.DelayBeforeFirstPass(1.0));
    }

    /// <summary>The alert is raised at the threshold, once, and not before it: the control for the raise case is the count below it.</summary>
    [Fact]
    public void The_alert_is_raised_on_the_third_failure_in_a_row_and_not_the_second_or_fourth()
    {
        Assert.Equal(LeaseRenewalAlertAction.None, LeaseRenewalPolicy.AlertActionFor(LeaseRenewalOutcome.Unreachable, 1, false));
        Assert.Equal(LeaseRenewalAlertAction.None, LeaseRenewalPolicy.AlertActionFor(LeaseRenewalOutcome.Unreachable, LeaseRenewalPolicy.AlertThreshold - 1, false));
        Assert.Equal(LeaseRenewalAlertAction.Raise, LeaseRenewalPolicy.AlertActionFor(LeaseRenewalOutcome.Unreachable, LeaseRenewalPolicy.AlertThreshold, false));
        Assert.Equal(LeaseRenewalAlertAction.None, LeaseRenewalPolicy.AlertActionFor(LeaseRenewalOutcome.Unreachable, LeaseRenewalPolicy.AlertThreshold + 1, true));
    }

    /// <summary>A stored lease resolves an alert that was raised and says nothing when none was.</summary>
    [Fact]
    public void A_stored_lease_resolves_a_raised_alert_and_only_a_raised_one()
    {
        Assert.Equal(LeaseRenewalAlertAction.Resolve, LeaseRenewalPolicy.AlertActionFor(LeaseRenewalOutcome.Renewed, 0, true));
        Assert.Equal(LeaseRenewalAlertAction.None, LeaseRenewalPolicy.AlertActionFor(LeaseRenewalOutcome.Renewed, 0, false));
    }

    /// <summary>A pass that is not a failure never raises, however long the run it follows was.</summary>
    [Fact]
    public void A_pass_that_is_not_a_failure_never_raises_the_alert()
    {
        Assert.Equal(
            LeaseRenewalAlertAction.None,
            LeaseRenewalPolicy.AlertActionFor(LeaseRenewalOutcome.NotDue, LeaseRenewalPolicy.AlertThreshold + 5, false));
        Assert.Equal(
            LeaseRenewalAlertAction.None,
            LeaseRenewalPolicy.AlertActionFor(LeaseRenewalOutcome.NotConfigured, LeaseRenewalPolicy.AlertThreshold + 5, false));
    }
}
