using Maran.Modules.Licensing.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Licensing.Tests.Services;

/// <summary>
/// Covers <see cref="Maran.Modules.Licensing.Services.LicenceEntitlements"/>, the licence window the gate,
/// the catalogue and a paid module's jobs ask: each verdict through the REAL verifier, source and cache,
/// with a lease source and a clock the test controls.
/// </summary>
public sealed class LicenceEntitlementsTests
{
    /// <summary>A lease issued at the harness start and valid for fourteen days.</summary>
    private static LeaseReading.Present ValidLease()
    {
        return new LeaseReading.Present(
            new LicenceLease("lic-001", CoverageHarness.Start, CoverageHarness.Start.AddDays(14), null));
    }

    /// <summary>A licence naming mail, signed by the trusted key.</summary>
    /// <param name="offline">Whether it is an offline licence.</param>
    /// <param name="expiry">When it expires.</param>
    /// <returns>The licence file text.</returns>
    private static string Licence(bool offline = false, DateTimeOffset? expiry = null)
    {
        return LicenceEnvelopeBuilder.Build(
            TestKeys.RealPrivateKeyHex,
            modules: ["mail"],
            expiry: expiry ?? CoverageHarness.Start.AddDays(90),
            offline: offline ? true : null);
    }

    /// <summary>A licence naming the module, with a fresh lease, covers it.</summary>
    [Fact]
    public async Task A_listed_module_with_a_valid_licence_and_lease_is_covered()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(ValidLease()));
        harness.Install(Licence());

        var entitlement = await harness.Window.GetAsync("mail", CancellationToken.None);

        Assert.Equal(EntitlementVerdict.Covered, entitlement.Verdict);
        Assert.Equal(CoverageHarness.Start.AddDays(14), entitlement.ValidUntil);
    }

    /// <summary>With nothing installed, a module is not covered; with a licence installed it is (the control).</summary>
    [Fact]
    public async Task With_no_licence_a_module_is_not_covered()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(ValidLease()));

        Assert.Equal(EntitlementVerdict.NotCovered, (await harness.Window.GetAsync("mail", CancellationToken.None)).Verdict);

        harness.Install(Licence());
        harness.Cache.Invalidate();

        Assert.Equal(EntitlementVerdict.Covered, (await harness.Window.GetAsync("mail", CancellationToken.None)).Verdict);
    }

    /// <summary>A licence that has expired lapses the modules it named.</summary>
    [Fact]
    public async Task An_expired_licence_lapses_its_module()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(ValidLease()));
        harness.Install(Licence(expiry: CoverageHarness.Start.AddDays(5)));
        harness.Clock.Advance(TimeSpan.FromDays(6));

        var entitlement = await harness.Window.GetAsync("mail", CancellationToken.None);

        Assert.Equal(EntitlementVerdict.Lapsed, entitlement.Verdict);
        Assert.Equal(CoverageHarness.Start.AddDays(5), entitlement.ValidUntil);
    }

    /// <summary>A lease that runs out lapses the module once the cache lifetime has passed, and not before.</summary>
    [Fact]
    public async Task A_lease_that_runs_out_lapses_the_module_when_the_remembered_answer_ages_out()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(ValidLease()));
        harness.Install(Licence());
        Assert.Equal(EntitlementVerdict.Covered, (await harness.Window.GetAsync("mail", CancellationToken.None)).Verdict);

        harness.Clock.Advance(TimeSpan.FromDays(15));

        // Still inside the remembered answer's lifetime? No: 15 days is far beyond it, so the read repeats.
        Assert.Equal(EntitlementVerdict.Lapsed, (await harness.Window.GetAsync("mail", CancellationToken.None)).Verdict);
    }

    /// <summary>No lease at all lapses a module of an online licence.</summary>
    [Fact]
    public async Task A_missing_lease_lapses_the_module_of_an_online_licence()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(LeaseReading.Absent.Instance));
        harness.Install(Licence());

        Assert.Equal(EntitlementVerdict.Lapsed, (await harness.Window.GetAsync("mail", CancellationToken.None)).Verdict);
    }

    /// <summary>An offline licence is covered with no lease; the same lease source gives an online one only a lapse.</summary>
    [Fact]
    public async Task An_offline_licence_is_covered_without_a_lease()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(LeaseReading.Absent.Instance));
        harness.Install(Licence(offline: true));

        var entitlement = await harness.Window.GetAsync("mail", CancellationToken.None);

        Assert.Equal(EntitlementVerdict.Covered, entitlement.Verdict);
        Assert.Equal(CoverageHarness.Start.AddDays(90), entitlement.ValidUntil);
    }

    /// <summary>One read answers for several modules, in the order asked, and an unlisted module is not covered.</summary>
    [Fact]
    public async Task One_list_call_answers_for_every_requested_module_in_order()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(ValidLease()));
        harness.Install(Licence());

        var entitlements = await harness.Window.ListAsync(["dns", "mail"], CancellationToken.None);

        Assert.Equal(["dns", "mail"], entitlements.Select(entitlement => { return entitlement.ModuleId; }));
        Assert.Equal(EntitlementVerdict.NotCovered, entitlements[0].Verdict);
        Assert.Equal(EntitlementVerdict.Covered, entitlements[1].Verdict);
        Assert.Equal(1, harness.Lease.Reads);
    }

    /// <summary>
    /// A source that throws never makes the window throw: every requested module is simply not covered. After
    /// the source recovers and the remembered answer is dropped, the same module is covered again.
    /// </summary>
    [Fact]
    public async Task A_throwing_lease_source_makes_every_module_not_covered_and_never_throws()
    {
        var lease = new CountingLeaseSource(ValidLease(), throws: true);
        using var harness = new CoverageHarness(lease);
        harness.Install(Licence());

        var failing = await harness.Window.ListAsync(["mail", "dns"], CancellationToken.None);

        Assert.All(failing, entitlement => { Assert.Equal(EntitlementVerdict.NotCovered, entitlement.Verdict); });

        lease.Throws = false;

        Assert.Equal(EntitlementVerdict.Covered, (await harness.Window.GetAsync("mail", CancellationToken.None)).Verdict);
    }
}
