using Maran.Modules.Licensing.Domain.Entities;
using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Domain.Policies;
using Maran.Modules.Licensing.Domain.ValueObjects;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Licensing.Tests.Domain.Policies;

/// <summary>
/// Covers <see cref="LeaseGracePolicy"/>: every standing, each boundary on both sides, and an inverse
/// control for each refusal. The policy reads the instant it is handed and nothing else.
/// </summary>
public sealed class LeaseGracePolicyTests
{
    /// <summary>This host's machine id in the tests.</summary>
    private const string ThisHost = "0123456789abcdef0123456789abcdef";

    /// <summary>Another server's machine id.</summary>
    private const string OtherHost = "fedcba9876543210fedcba9876543210";

    /// <summary>The instant a lease in these tests was issued.</summary>
    private static readonly DateTimeOffset Issued = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A lease issued at <see cref="Issued"/> and valid for fourteen days.</summary>
    private static readonly LicenceLease Lease = new("lic-001", Issued, Issued.AddDays(14), null);

    /// <summary>Builds the licence a lease renews.</summary>
    /// <param name="bound">The machine id the licence is bound to, or null.</param>
    /// <param name="offline">Whether it is an offline licence.</param>
    /// <returns>The licence.</returns>
    private static Licence LicenceFor(string? bound = null, bool offline = false)
    {
        return new Licence(
            LicenceId.Of("lic-001"),
            "maran",
            "included",
            ["mail"],
            Issued.AddYears(1),
            bound,
            offline);
    }

    /// <summary>Judges a verified lease at an instant.</summary>
    /// <param name="lease">The lease.</param>
    /// <param name="now">The instant.</param>
    /// <param name="licence">The licence, defaulting to an unbound online one.</param>
    /// <param name="host">This host's machine id.</param>
    /// <returns>The standing.</returns>
    private static LeaseStanding Judge(LicenceLease lease, DateTimeOffset now, Licence? licence = null, string? host = ThisHost)
    {
        return LeaseGracePolicy.StandingOf(new LeaseReading.Present(lease), licence ?? LicenceFor(), host, now);
    }

    /// <summary>A lease issued moments ago is fresh, and says when it ends.</summary>
    [Fact]
    public void A_lease_issued_moments_ago_is_fresh_and_reports_when_it_ends()
    {
        var standing = Judge(Lease, Issued.AddHours(1));

        Assert.Equal(LeaseStandingKind.Fresh, standing.Kind);
        Assert.Equal(Lease.ValidUntil, standing.ValidUntil);
    }

    /// <summary>The renewal interval is a boundary: one tick short is fresh, the interval itself is overdue.</summary>
    [Fact]
    public void A_lease_is_fresh_until_the_renewal_interval_and_overdue_from_it()
    {
        Assert.Equal(
            LeaseStandingKind.Fresh,
            Judge(Lease, Issued + LeaseGracePolicy.RenewalInterval - TimeSpan.FromTicks(1)).Kind);
        Assert.Equal(
            LeaseStandingKind.RenewalOverdue,
            Judge(Lease, Issued + LeaseGracePolicy.RenewalInterval).Kind);
    }

    /// <summary>An overdue renewal still holds the cover: the grace is the lease's life.</summary>
    [Fact]
    public void A_lease_older_than_a_day_but_inside_its_life_is_overdue_and_still_ends_on_its_own_date()
    {
        var standing = Judge(Lease, Issued.AddDays(13));

        Assert.Equal(LeaseStandingKind.RenewalOverdue, standing.Kind);
        Assert.Equal(Lease.ValidUntil, standing.ValidUntil);
    }

    /// <summary>The end is a boundary: the instant before it holds, the instant itself has expired.</summary>
    [Fact]
    public void A_lease_expires_exactly_at_its_valid_until()
    {
        Assert.Equal(
            LeaseStandingKind.RenewalOverdue,
            Judge(Lease, Lease.ValidUntil - TimeSpan.FromTicks(1)).Kind);

        var expired = Judge(Lease, Lease.ValidUntil);

        Assert.Equal(LeaseStandingKind.Expired, expired.Kind);
        Assert.Equal(Lease.ValidUntil, expired.ValidUntil);
    }

    /// <summary>The skew bound is a boundary: an issue time exactly at the bound is believed, beyond it is forged.</summary>
    [Fact]
    public void A_lease_issued_in_the_future_beyond_the_skew_bound_is_refused_and_inside_it_is_believed()
    {
        var now = Issued.AddDays(-1);
        var atBound = new LicenceLease("lic-001", now + LeaseGracePolicy.SkewBound, now.AddDays(14), null);
        var beyond = new LicenceLease("lic-001", now + LeaseGracePolicy.SkewBound + TimeSpan.FromTicks(1), now.AddDays(14), null);

        Assert.Equal(LeaseStandingKind.Fresh, Judge(atBound, now).Kind);
        Assert.Equal(LeaseStandingKind.FutureDated, Judge(beyond, now).Kind);
    }

    /// <summary>A lease for another licence is wrong whatever its dates say, even when it has also expired.</summary>
    [Fact]
    public void A_lease_for_another_licence_is_refused_before_its_dates_are_read()
    {
        var other = new LicenceLease("lic-999", Issued, Issued.AddDays(14), null);

        Assert.Equal(LeaseStandingKind.WrongLicence, Judge(other, Issued.AddHours(1)).Kind);
        Assert.Equal(LeaseStandingKind.WrongLicence, Judge(other, Issued.AddDays(30)).Kind);
        Assert.Equal(LeaseStandingKind.Fresh, Judge(Lease, Issued.AddHours(1)).Kind);
    }

    /// <summary>A lease naming another server is refused; one naming this server is accepted.</summary>
    [Fact]
    public void A_lease_bound_to_another_server_is_refused_and_one_bound_to_this_server_is_accepted()
    {
        var forOther = new LicenceLease("lic-001", Issued, Issued.AddDays(14), OtherHost);
        var forThis = new LicenceLease("lic-001", Issued, Issued.AddDays(14), ThisHost.ToUpperInvariant());

        Assert.Equal(LeaseStandingKind.WrongMachine, Judge(forOther, Issued.AddHours(1)).Kind);
        Assert.Equal(LeaseStandingKind.Fresh, Judge(forThis, Issued.AddHours(1)).Kind);
    }

    /// <summary>A licence bound to another server refuses even an unbound lease; bound to this one it accepts it.</summary>
    [Fact]
    public void A_licence_bound_to_another_server_refuses_the_lease_and_one_bound_to_this_server_accepts_it()
    {
        Assert.Equal(
            LeaseStandingKind.WrongMachine,
            Judge(Lease, Issued.AddHours(1), LicenceFor(bound: OtherHost)).Kind);
        Assert.Equal(
            LeaseStandingKind.Fresh,
            Judge(Lease, Issued.AddHours(1), LicenceFor(bound: ThisHost)).Kind);
    }

    /// <summary>A lease that names a server cannot match a host whose machine id could not be read.</summary>
    [Fact]
    public void A_lease_naming_a_server_matches_nothing_when_the_host_is_unreadable()
    {
        var named = new LicenceLease("lic-001", Issued, Issued.AddDays(14), ThisHost);

        Assert.Equal(LeaseStandingKind.WrongMachine, Judge(named, Issued.AddHours(1), host: null).Kind);
        Assert.Equal(LeaseStandingKind.Fresh, Judge(Lease, Issued.AddHours(1), host: null).Kind);
    }

    /// <summary>No lease and an untrusted lease are different standings, and neither is fresh.</summary>
    [Fact]
    public void An_absent_lease_is_missing_and_a_refused_one_is_refused()
    {
        var now = Issued.AddHours(1);

        Assert.Equal(
            LeaseStandingKind.Missing,
            LeaseGracePolicy.StandingOf(LeaseReading.Absent.Instance, LicenceFor(), ThisHost, now).Kind);
        Assert.Equal(
            LeaseStandingKind.Refused,
            LeaseGracePolicy.StandingOf(
                new LeaseReading.Refused(LeaseRefusalReason.SignatureInvalid),
                LicenceFor(),
                ThisHost,
                now).Kind);
    }

    /// <summary>An offline licence asks for no lease, whatever the reading; an online one does not get the pass.</summary>
    [Fact]
    public void An_offline_licence_needs_no_lease_and_an_online_one_does()
    {
        var now = Issued.AddHours(1);

        Assert.Equal(
            LeaseStandingKind.NotRequired,
            LeaseGracePolicy.StandingOf(LeaseReading.Absent.Instance, LicenceFor(offline: true), ThisHost, now).Kind);
        Assert.Equal(
            LeaseStandingKind.Missing,
            LeaseGracePolicy.StandingOf(LeaseReading.Absent.Instance, LicenceFor(offline: false), ThisHost, now).Kind);
    }
}
