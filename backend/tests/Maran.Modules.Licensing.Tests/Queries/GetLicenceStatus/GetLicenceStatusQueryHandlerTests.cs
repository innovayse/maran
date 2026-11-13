using Maran.Modules.Licensing.Common;
using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Queries.GetLicenceStatus;
using Maran.Modules.Licensing.Services;
using Maran.Modules.Licensing.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Licensing.Tests.Queries.GetLicenceStatus;

/// <summary>
/// Covers what <see cref="GetLicenceStatusQueryHandler"/> tells the administrator about THIS server beside the
/// licence: its id and the link to the claim page, in every state, and no request for a lease on a read.
/// </summary>
public sealed class GetLicenceStatusQueryHandlerTests
{
    /// <summary>A machine id as the marketplace spells one.</summary>
    private const string ServerId = "0123456789abcdef0123456789abcdef";

    /// <summary>Builds the handler over a harness whose host reports <paramref name="machineId"/>.</summary>
    /// <param name="harness">The harness whose uncached source it reads.</param>
    /// <param name="machineId">The machine id the host reports, or null.</param>
    /// <returns>The handler.</returns>
    private static GetLicenceStatusQueryHandler HandlerOver(CoverageHarness harness, string? machineId)
    {
        return new GetLicenceStatusQueryHandler(
            harness.Source,
            LicensingTestContext.StatusText(),
            new LicensingAuditJournal(new RecordingAuditWriter(), new FakeCurrentUser(isAdmin: true)),
            LicensingTestContext.ServerFacts(machineId),
            LicensingTestContext.ExpiryWarnings(harness.Clock));
    }

    /// <summary>Reads the status.</summary>
    /// <param name="handler">The handler.</param>
    /// <returns>The status the administrator reads.</returns>
    private static async Task<LicenceStatusDto> ReadAsync(GetLicenceStatusQueryHandler handler)
    {
        var result = await handler.HandleAsync(new GetLicenceStatusQuery("127.0.0.1", "test-agent"), CancellationToken.None);

        return result.Value!;
    }

    /// <summary>With no licence installed the status still names the server and links to the claim page, because that is when it is needed.</summary>
    [Fact]
    public async Task A_panel_with_no_licence_still_shows_the_server_id_and_the_claim_link()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(LeaseReading.Absent.Instance), ServerId);

        var status = await ReadAsync(HandlerOver(harness, ServerId));

        Assert.Equal(LicenceStateKind.Absent, status.State);
        Assert.Equal(ServerId, status.ServerId);
        Assert.Equal("https://plugins-maran.innovayse.com/claim?server=" + ServerId, status.ClaimUrl);
    }

    /// <summary>A valid licence and a refused one carry the same facts about the server.</summary>
    [Fact]
    public async Task A_valid_and_a_refused_licence_carry_the_server_id_too()
    {
        using var valid = new CoverageHarness(new CountingLeaseSource(LeaseReading.Absent.Instance), ServerId);
        valid.Install(LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex));
        using var refused = new CoverageHarness(new CountingLeaseSource(LeaseReading.Absent.Instance), ServerId);
        refused.Install(LicenceEnvelopeBuilder.Build(TestKeys.OtherPrivateKeyHex));

        var validStatus = await ReadAsync(HandlerOver(valid, ServerId));
        var refusedStatus = await ReadAsync(HandlerOver(refused, ServerId));

        Assert.Equal(LicenceStateKind.Valid, validStatus.State);
        Assert.Equal(ServerId, validStatus.ServerId);
        Assert.Equal(LicenceStateKind.Refused, refusedStatus.State);
        Assert.Equal(ServerId, refusedStatus.ServerId);
    }

    /// <summary>A host whose id cannot be read has neither an id nor a link; nothing is invented. The tests above are the control.</summary>
    [Fact]
    public async Task A_host_whose_id_cannot_be_read_has_neither_id_nor_link()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(LeaseReading.Absent.Instance));

        var status = await ReadAsync(HandlerOver(harness, null));

        Assert.Null(status.ServerId);
        Assert.Null(status.ClaimUrl);
    }

    /// <summary>A plain read asks nothing of the licence service, so it reports no lease request.</summary>
    [Fact]
    public async Task A_plain_read_reports_no_lease_request()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(LeaseReading.Absent.Instance), ServerId);

        var status = await ReadAsync(HandlerOver(harness, ServerId));

        Assert.Null(status.LeaseRequest);
        Assert.Null(status.LeaseRequestDisplayName);
    }

    /// <summary>A valid licence with exactly 30 days left carries the warning, the sentence naming the expiry day, the steps and the days left.</summary>
    [Fact]
    public async Task A_licence_within_thirty_days_carries_the_expiry_warning_and_the_renewal_steps()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(LeaseReading.Absent.Instance), ServerId);
        harness.Install(LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, expiry: CoverageHarness.Start.AddDays(30)));

        var status = await ReadAsync(HandlerOver(harness, ServerId));

        Assert.Equal(30, status.ExpiryWarningDaysLeft);
        Assert.Equal("The installed licence expires on 2026-01-31 (days left: 30).", status.ExpiryWarning);
        Assert.Contains("claim the licence again", status.ExpiryRenewalAdvice, StringComparison.Ordinal);
        Assert.Contains("install a new licence file", status.ExpiryRenewalAdvice, StringComparison.Ordinal);
        Assert.Equal("https://plugins-maran.innovayse.com/claim?server=" + ServerId, status.ClaimUrl);
    }

    /// <summary>One second outside the 30-day threshold there is no warning at all; the case above is the control.</summary>
    [Fact]
    public async Task A_licence_just_outside_thirty_days_carries_no_warning()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(LeaseReading.Absent.Instance), ServerId);
        harness.Install(
            LicenceEnvelopeBuilder.Build(
                TestKeys.RealPrivateKeyHex,
                expiry: CoverageHarness.Start.AddDays(30).AddSeconds(1)));

        var status = await ReadAsync(HandlerOver(harness, ServerId));

        Assert.Equal(LicenceStateKind.Valid, status.State);
        Assert.Null(status.ExpiryWarningDaysLeft);
        Assert.Null(status.ExpiryWarning);
        Assert.Null(status.ExpiryRenewalAdvice);
    }

    /// <summary>Moving the clock through the thresholds changes the days left, and installing a licence further out clears the warning.</summary>
    [Fact]
    public async Task Installing_a_licence_further_out_clears_the_warning()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(LeaseReading.Absent.Instance), ServerId);
        harness.Install(LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, expiry: CoverageHarness.Start.AddDays(10)));
        var handler = HandlerOver(harness, ServerId);
        Assert.Equal(10, (await ReadAsync(handler)).ExpiryWarningDaysLeft);

        harness.Install(LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, expiry: CoverageHarness.Start.AddDays(365)));
        var after = await ReadAsync(handler);

        Assert.Equal(LicenceStateKind.Valid, after.State);
        Assert.Null(after.ExpiryWarningDaysLeft);
        Assert.Null(after.ExpiryWarning);
    }

    /// <summary>An expired licence is a refusal with no warning; no licence at all has none either.</summary>
    [Fact]
    public async Task An_expired_licence_and_no_licence_carry_no_warning()
    {
        using var expired = new CoverageHarness(new CountingLeaseSource(LeaseReading.Absent.Instance), ServerId);
        expired.Install(LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, expiry: CoverageHarness.Start.AddDays(5)));
        expired.Clock.Advance(TimeSpan.FromDays(6));
        using var none = new CoverageHarness(new CountingLeaseSource(LeaseReading.Absent.Instance), ServerId);

        var expiredStatus = await ReadAsync(HandlerOver(expired, ServerId));
        var noneStatus = await ReadAsync(HandlerOver(none, ServerId));

        Assert.Equal(LicenceStateKind.Refused, expiredStatus.State);
        Assert.Null(expiredStatus.ExpiryWarningDaysLeft);
        Assert.Equal(LicenceStateKind.Absent, noneStatus.State);
        Assert.Null(noneStatus.ExpiryWarningDaysLeft);
    }
}
