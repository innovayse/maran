using Maran.Modules.Licensing.Services;
using Maran.Modules.Licensing.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Licensing.Tests.Services;

/// <summary>
/// Covers <see cref="LicenceStatusLine"/>, the one line <c>maran status</c> prints: each state in the words
/// the status screen uses, and no licence text, signature or machine id in any of them.
/// </summary>
public sealed class LicenceStatusLineTests
{
    /// <summary>Builds the describer over a harness.</summary>
    /// <param name="harness">The harness whose source it reads.</param>
    /// <returns>The describer.</returns>
    private static LicenceStatusLine LineOver(CoverageHarness harness)
    {
        return new LicenceStatusLine(harness.Source, LicensingTestContext.StatusText());
    }

    /// <summary>Nothing installed reads as "no licence installed".</summary>
    [Fact]
    public async Task No_licence_reads_as_none_installed()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(LeaseReading.Absent.Instance));

        Assert.Equal("No licence installed", await LineOver(harness).DescribeAsync(CancellationToken.None));
    }

    /// <summary>A valid licence reads with its tier, its expiry day and its lease; the licence id and the signature are absent.</summary>
    [Fact]
    public async Task A_valid_licence_reads_with_tier_expiry_and_lease_and_nothing_secret()
    {
        var lease = new LicenceLease("lic-secret-id", CoverageHarness.Start, CoverageHarness.Start.AddDays(14), null);
        using var harness = new CoverageHarness(new CountingLeaseSource(new LeaseReading.Present(lease)));
        var text = LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, id: "lic-secret-id", tier: "business");
        harness.Install(text);

        var line = await LineOver(harness).DescribeAsync(CancellationToken.None);

        Assert.Equal("Licence installed: business, 2026-01-31; Lease current", line);
        Assert.DoesNotContain("lic-secret-id", line, StringComparison.Ordinal);
    }

    /// <summary>A refused licence reads as refused with the reason's short name.</summary>
    [Fact]
    public async Task A_refused_licence_reads_with_the_reasons_name()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(LeaseReading.Absent.Instance));
        harness.Install(LicenceEnvelopeBuilder.Build(TestKeys.OtherPrivateKeyHex));

        var line = await LineOver(harness).DescribeAsync(CancellationToken.None);

        Assert.StartsWith("Licence not accepted: ", line, StringComparison.Ordinal);
    }
}
