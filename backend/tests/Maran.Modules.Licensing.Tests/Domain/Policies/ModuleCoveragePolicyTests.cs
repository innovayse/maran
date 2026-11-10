using Maran.Modules.Licensing.Domain.Entities;
using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Domain.Policies;
using Maran.Modules.Licensing.Domain.ValueObjects;
using Maran.Modules.Licensing.Models;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Licensing.Tests.Domain.Policies;

/// <summary>
/// Covers <see cref="ModuleCoveragePolicy"/>, the one rule behind the gate, the catalogue and a paid
/// module's jobs: each verdict, with an accepting control beside every refusal.
/// </summary>
public sealed class ModuleCoveragePolicyTests
{
    /// <summary>When the licence in these tests expires.</summary>
    private static readonly DateTimeOffset LicenceExpiry = new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>When the lease in these tests ends, earlier than the licence.</summary>
    private static readonly DateTimeOffset LeaseEnd = new(2026, 3, 15, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Builds a licence naming the given modules.</summary>
    /// <param name="offline">Whether it is an offline licence.</param>
    /// <param name="modules">The module ids its signed list names.</param>
    /// <returns>The licence.</returns>
    private static Licence LicenceNaming(bool offline, params string[] modules)
    {
        return new Licence(LicenceId.Of("lic-001"), "maran", "included", modules, LicenceExpiry, null, offline);
    }

    /// <summary>Builds a coverage over a valid licence and a lease standing.</summary>
    /// <param name="licence">The licence.</param>
    /// <param name="lease">The standing of its lease.</param>
    /// <returns>The coverage.</returns>
    private static LicenceCoverage Valid(Licence licence, LeaseStanding lease)
    {
        return new LicenceCoverage(new LicenceStatus.Valid(licence), lease);
    }

    /// <summary>A module the licence names, under a fresh lease, is covered until the earlier of the two ends.</summary>
    [Fact]
    public void A_listed_module_under_a_fresh_lease_is_covered_until_the_lease_ends()
    {
        var coverage = Valid(
            LicenceNaming(false, "mail"),
            new LeaseStanding(LeaseStandingKind.Fresh, LeaseEnd));

        var entitlement = ModuleCoveragePolicy.VerdictOf(coverage, "mail");

        Assert.Equal(EntitlementVerdict.Covered, entitlement.Verdict);
        Assert.Equal(LeaseEnd, entitlement.ValidUntil);
    }

    /// <summary>A lease that outlives the licence does not extend it: the licence's expiry is the end.</summary>
    [Fact]
    public void A_lease_longer_than_the_licence_does_not_extend_the_cover()
    {
        var coverage = Valid(
            LicenceNaming(false, "mail"),
            new LeaseStanding(LeaseStandingKind.RenewalOverdue, LicenceExpiry.AddDays(10)));

        Assert.Equal(LicenceExpiry, ModuleCoveragePolicy.VerdictOf(coverage, "mail").ValidUntil);
    }

    /// <summary>A module the licence does not name is not covered, and the named one beside it still is.</summary>
    [Fact]
    public void A_module_the_licence_does_not_name_is_not_covered()
    {
        var coverage = Valid(
            LicenceNaming(false, "mail"),
            new LeaseStanding(LeaseStandingKind.Fresh, LeaseEnd));

        Assert.Equal(EntitlementVerdict.NotCovered, ModuleCoveragePolicy.VerdictOf(coverage, "dns").Verdict);
        Assert.Equal(EntitlementVerdict.Covered, ModuleCoveragePolicy.VerdictOf(coverage, "mail").Verdict);
    }

    /// <summary>The module list is matched exactly: a different case is a different module.</summary>
    [Fact]
    public void A_module_id_is_matched_exactly()
    {
        var coverage = Valid(
            LicenceNaming(false, "mail"),
            new LeaseStanding(LeaseStandingKind.Fresh, LeaseEnd));

        Assert.Equal(EntitlementVerdict.NotCovered, ModuleCoveragePolicy.VerdictOf(coverage, "Mail").Verdict);
    }

    /// <summary>A lease that is missing or has expired makes a listed module lapse, not vanish.</summary>
    [Theory]
    [InlineData(LeaseStandingKind.Missing)]
    [InlineData(LeaseStandingKind.Expired)]
    public void A_missing_or_expired_lease_lapses_a_listed_module(LeaseStandingKind kind)
    {
        var coverage = Valid(LicenceNaming(false, "mail"), new LeaseStanding(kind, null));

        Assert.Equal(EntitlementVerdict.Lapsed, ModuleCoveragePolicy.VerdictOf(coverage, "mail").Verdict);
    }

    /// <summary>A lease nothing can rely on covers nothing, whatever the licence says.</summary>
    [Theory]
    [InlineData(LeaseStandingKind.Refused)]
    [InlineData(LeaseStandingKind.FutureDated)]
    [InlineData(LeaseStandingKind.WrongLicence)]
    [InlineData(LeaseStandingKind.WrongMachine)]
    public void An_untrustworthy_lease_covers_nothing(LeaseStandingKind kind)
    {
        var coverage = Valid(LicenceNaming(false, "mail"), new LeaseStanding(kind, null));

        Assert.Equal(EntitlementVerdict.NotCovered, ModuleCoveragePolicy.VerdictOf(coverage, "mail").Verdict);
    }

    /// <summary>An offline licence is covered by its own expiry with no lease, even when none is stored.</summary>
    [Fact]
    public void An_offline_licence_covers_a_listed_module_without_a_lease()
    {
        var coverage = Valid(LicenceNaming(true, "mail"), LeaseStanding.NotRequired);

        var entitlement = ModuleCoveragePolicy.VerdictOf(coverage, "mail");

        Assert.Equal(EntitlementVerdict.Covered, entitlement.Verdict);
        Assert.Equal(LicenceExpiry, entitlement.ValidUntil);
    }

    /// <summary>An online licence with no lease is NOT given the offline pass: the control for the test above.</summary>
    [Fact]
    public void An_online_licence_without_a_lease_is_not_given_the_offline_pass()
    {
        var coverage = Valid(LicenceNaming(false, "mail"), LeaseStanding.Missing);

        Assert.NotEqual(EntitlementVerdict.Covered, ModuleCoveragePolicy.VerdictOf(coverage, "mail").Verdict);
    }

    /// <summary>An expired licence lapses the modules it named, and only those.</summary>
    [Fact]
    public void An_expired_licence_lapses_the_modules_it_named_and_no_others()
    {
        var expired = LicenceNaming(false, "mail");
        var coverage = new LicenceCoverage(
            new LicenceStatus.Refused(LicenceRefusalReason.Expired, expired),
            LeaseStanding.Missing);

        var named = ModuleCoveragePolicy.VerdictOf(coverage, "mail");

        Assert.Equal(EntitlementVerdict.Lapsed, named.Verdict);
        Assert.Equal(LicenceExpiry, named.ValidUntil);
        Assert.Equal(EntitlementVerdict.NotCovered, ModuleCoveragePolicy.VerdictOf(coverage, "dns").Verdict);
    }

    /// <summary>Nothing installed, or a licence refused for any other reason, covers nothing.</summary>
    [Fact]
    public void No_licence_and_a_refused_licence_cover_nothing()
    {
        var absent = new LicenceCoverage(LicenceStatus.Absent.Instance, LeaseStanding.Missing);
        var forged = new LicenceCoverage(
            new LicenceStatus.Refused(LicenceRefusalReason.SignatureInvalid),
            LeaseStanding.Missing);
        var wrongServer = new LicenceCoverage(
            new LicenceStatus.Refused(LicenceRefusalReason.FingerprintMismatch),
            LeaseStanding.Missing);

        Assert.Equal(EntitlementVerdict.NotCovered, ModuleCoveragePolicy.VerdictOf(absent, "mail").Verdict);
        Assert.Equal(EntitlementVerdict.NotCovered, ModuleCoveragePolicy.VerdictOf(forged, "mail").Verdict);
        Assert.Equal(EntitlementVerdict.NotCovered, ModuleCoveragePolicy.VerdictOf(wrongServer, "mail").Verdict);
    }
}
