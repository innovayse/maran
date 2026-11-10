using Maran.Modules.Licensing.Services;
using Maran.Modules.Licensing.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Licensing.Tests.Services;

/// <summary>
/// Covers <see cref="LicenceCoverageCache"/>: it reads once inside its lifetime, reads again after it,
/// and forgets on invalidation. The lifetime is driven by a clock the test moves; nothing sleeps.
/// </summary>
public sealed class LicenceCoverageCacheTests
{
    /// <summary>Builds a harness with a counting lease source and a licence installed.</summary>
    /// <param name="lease">The counting lease source.</param>
    /// <returns>The harness.</returns>
    private static CoverageHarness HarnessWith(CountingLeaseSource lease)
    {
        var harness = new CoverageHarness(lease);
        harness.Install(LicenceEnvelopeBuilder.Build(
            TestKeys.RealPrivateKeyHex,
            modules: ["mail"],
            expiry: CoverageHarness.Start.AddDays(90)));

        return harness;
    }

    /// <summary>A lease source answering a lease valid for the whole of these tests.</summary>
    /// <returns>The source.</returns>
    private static CountingLeaseSource LongLease()
    {
        return new CountingLeaseSource(
            new LeaseReading.Present(
                new LicenceLease("lic-001", CoverageHarness.Start, CoverageHarness.Start.AddDays(14), null)));
    }

    /// <summary>Two reads inside the lifetime cost one read of the lease.</summary>
    [Fact]
    public async Task Two_reads_inside_the_lifetime_read_the_licence_once()
    {
        var lease = LongLease();
        using var harness = HarnessWith(lease);

        await harness.Cache.GetAsync(CancellationToken.None);
        harness.Clock.Advance(LicenceCoverageCache.TimeToLive - TimeSpan.FromTicks(1));
        await harness.Cache.GetAsync(CancellationToken.None);

        Assert.Equal(1, lease.Reads);
    }

    /// <summary>A read at the lifetime repeats the read: the boundary is exclusive of the old answer.</summary>
    [Fact]
    public async Task A_read_at_the_lifetime_reads_the_licence_again()
    {
        var lease = LongLease();
        using var harness = HarnessWith(lease);

        await harness.Cache.GetAsync(CancellationToken.None);
        harness.Clock.Advance(LicenceCoverageCache.TimeToLive);
        await harness.Cache.GetAsync(CancellationToken.None);

        Assert.Equal(2, lease.Reads);
    }

    /// <summary>Invalidation makes the very next read see the file as it now is.</summary>
    [Fact]
    public async Task Invalidation_makes_the_next_read_see_the_current_file()
    {
        var lease = LongLease();
        using var harness = HarnessWith(lease);
        var before = await harness.Window.GetAsync("dns", CancellationToken.None);

        harness.Install(LicenceEnvelopeBuilder.Build(
            TestKeys.RealPrivateKeyHex,
            modules: ["mail", "dns"],
            expiry: CoverageHarness.Start.AddDays(90)));
        var stale = await harness.Window.GetAsync("dns", CancellationToken.None);
        harness.Cache.Invalidate();
        var fresh = await harness.Window.GetAsync("dns", CancellationToken.None);

        Assert.Equal(EntitlementVerdict.NotCovered, before.Verdict);
        Assert.Equal(EntitlementVerdict.NotCovered, stale.Verdict);
        Assert.Equal(EntitlementVerdict.Covered, fresh.Verdict);
    }
}
