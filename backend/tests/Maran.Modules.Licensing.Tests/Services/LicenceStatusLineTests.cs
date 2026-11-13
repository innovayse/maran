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
    /// <summary>A machine id as the marketplace spells one: 32 lower-case hexadecimal digits.</summary>
    private const string ServerId = "0123456789abcdef0123456789abcdef";

    /// <summary>Builds the describer over a harness.</summary>
    /// <param name="harness">The harness whose source it reads.</param>
    /// <param name="machineId">The machine id the host reports, or null for a host whose id cannot be read.</param>
    /// <returns>The describer.</returns>
    private static LicenceStatusLine LineOver(CoverageHarness harness, string? machineId = null)
    {
        return new LicenceStatusLine(
            harness.Source,
            LicensingTestContext.StatusText(),
            LicensingTestContext.ServerFacts(machineId),
            LicensingTestContext.ExpiryWarnings(harness.Clock));
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

    /// <summary>The server line shows the machine id exactly as the host reports it, with the label the screen uses.</summary>
    [Fact]
    public async Task The_server_line_shows_the_id_the_licence_is_bound_by()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(LeaseReading.Absent.Instance));

        var line = await LineOver(harness, ServerId).DescribeServerAsync(CancellationToken.None);

        Assert.Equal("Server ID: " + ServerId, line);
    }

    /// <summary>A host whose id cannot be read says so instead of printing a blank; the line above is the control.</summary>
    [Fact]
    public async Task The_server_line_says_unavailable_when_the_host_has_no_readable_id()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(LeaseReading.Absent.Instance));

        var line = await LineOver(harness).DescribeServerAsync(CancellationToken.None);

        Assert.Equal("Server ID: unavailable", line);
    }

    /// <summary>The licence line itself still carries no machine id, even on a host that has one.</summary>
    [Fact]
    public async Task The_licence_line_does_not_carry_the_machine_id()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(LeaseReading.Absent.Instance), ServerId);
        harness.Install(LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, tier: "business"));

        var line = await LineOver(harness, ServerId).DescribeAsync(CancellationToken.None);

        Assert.DoesNotContain(ServerId, line, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A licence within a threshold gets one warning line: expiry day, days left, the steps and the claim link.</summary>
    [Fact]
    public async Task A_licence_within_a_threshold_gets_a_warning_line_with_the_steps_and_the_claim_link()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(LeaseReading.Absent.Instance), ServerId);
        harness.Install(LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, expiry: CoverageHarness.Start.AddDays(14)));

        var line = await LineOver(harness, ServerId).DescribeExpiryWarningAsync(CancellationToken.None);

        Assert.NotNull(line);
        Assert.StartsWith("The installed licence expires on 2026-01-15 (days left: 14). ", line, StringComparison.Ordinal);
        Assert.Contains("claim the licence again", line, StringComparison.Ordinal);
        Assert.EndsWith("https://plugins-maran.innovayse.com/claim?server=" + ServerId, line, StringComparison.Ordinal);
    }

    /// <summary>A host with no readable id still gets the warning and the steps, without a link.</summary>
    [Fact]
    public async Task A_host_without_an_id_gets_the_warning_without_a_link()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(LeaseReading.Absent.Instance));
        harness.Install(LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, expiry: CoverageHarness.Start.AddDays(3)));

        var line = await LineOver(harness).DescribeExpiryWarningAsync(CancellationToken.None);

        Assert.NotNull(line);
        Assert.DoesNotContain("https://", line, StringComparison.Ordinal);
    }

    /// <summary>Outside the widest threshold, expired, refused and absent licences print no warning line.</summary>
    [Fact]
    public async Task No_warning_line_outside_the_window_or_for_an_expired_refused_or_absent_licence()
    {
        using var outside = new CoverageHarness(new CountingLeaseSource(LeaseReading.Absent.Instance), ServerId);
        outside.Install(
            LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, expiry: CoverageHarness.Start.AddDays(30).AddSeconds(1)));
        using var expired = new CoverageHarness(new CountingLeaseSource(LeaseReading.Absent.Instance), ServerId);
        expired.Install(LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, expiry: CoverageHarness.Start.AddDays(2)));
        expired.Clock.Advance(TimeSpan.FromDays(3));
        using var refused = new CoverageHarness(new CountingLeaseSource(LeaseReading.Absent.Instance), ServerId);
        refused.Install(LicenceEnvelopeBuilder.Build(TestKeys.OtherPrivateKeyHex, expiry: CoverageHarness.Start.AddDays(2)));
        using var absent = new CoverageHarness(new CountingLeaseSource(LeaseReading.Absent.Instance), ServerId);

        Assert.Null(await LineOver(outside, ServerId).DescribeExpiryWarningAsync(CancellationToken.None));
        Assert.Null(await LineOver(expired, ServerId).DescribeExpiryWarningAsync(CancellationToken.None));
        Assert.Null(await LineOver(refused, ServerId).DescribeExpiryWarningAsync(CancellationToken.None));
        Assert.Null(await LineOver(absent, ServerId).DescribeExpiryWarningAsync(CancellationToken.None));
    }
}
