using Maran.Modules.Licensing.Queries.CountExpiringLicence;
using Maran.Modules.Licensing.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Licensing.Tests.Queries.CountExpiringLicence;

/// <summary>
/// Covers <see cref="CountExpiringLicenceQueryHandler"/>: the dashboard's figure is 1 exactly while the installed
/// licence is within a warning threshold, read through the same cache the gate uses.
/// </summary>
public sealed class CountExpiringLicenceQueryHandlerTests
{
    /// <summary>Reads the figure over a harness.</summary>
    /// <param name="harness">The harness whose cache is read.</param>
    /// <returns>The count.</returns>
    private static async Task<int> CountAsync(CoverageHarness harness)
    {
        var handler = new CountExpiringLicenceQueryHandler(
            harness.Cache,
            LicensingTestContext.ExpiryWarnings(harness.Clock));

        var result = await handler.HandleAsync(new CountExpiringLicenceQuery(), CancellationToken.None);

        return result.Value;
    }

    /// <summary>A licence inside the window counts as 1; the licence just outside it, an expired one and no licence count as 0.</summary>
    [Fact]
    public async Task Only_a_licence_inside_a_threshold_counts()
    {
        using var inside = new CoverageHarness(new CountingLeaseSource(LeaseReading.Absent.Instance));
        inside.Install(LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, expiry: CoverageHarness.Start.AddDays(30)));
        using var outside = new CoverageHarness(new CountingLeaseSource(LeaseReading.Absent.Instance));
        outside.Install(
            LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, expiry: CoverageHarness.Start.AddDays(30).AddSeconds(1)));
        using var expired = new CoverageHarness(new CountingLeaseSource(LeaseReading.Absent.Instance));
        expired.Install(LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, expiry: CoverageHarness.Start.AddDays(2)));
        expired.Clock.Advance(TimeSpan.FromDays(3));
        using var none = new CoverageHarness(new CountingLeaseSource(LeaseReading.Absent.Instance));

        Assert.Equal(1, await CountAsync(inside));
        Assert.Equal(0, await CountAsync(outside));
        Assert.Equal(0, await CountAsync(expired));
        Assert.Equal(0, await CountAsync(none));
    }
}
