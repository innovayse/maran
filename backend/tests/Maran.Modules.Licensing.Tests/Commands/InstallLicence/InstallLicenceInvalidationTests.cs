using Maran.Modules.Licensing.Commands.InstallLicence;
using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Options;
using Maran.Modules.Licensing.Services;
using Maran.Modules.Licensing.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Licensing.Tests.Commands.InstallLicence;

/// <summary>
/// Covers what installing a licence does to the licence window: a successful install makes the very next
/// question see the new verdict, a refused one leaves the old verdict standing, and the answer reports the
/// lease's standing beside the licence.
/// </summary>
public sealed class InstallLicenceInvalidationTests
{
    /// <summary>A lease valid for the whole of these tests.</summary>
    private static CountingLeaseSource ValidLeaseSource()
    {
        return new CountingLeaseSource(
            new LeaseReading.Present(
                new LicenceLease("lic-001", CoverageHarness.Start, CoverageHarness.Start.AddDays(14), null)));
    }

    /// <summary>Builds the install handler over the harness's own source and cache.</summary>
    /// <param name="harness">The harness whose cache the handler invalidates.</param>
    /// <returns>The handler.</returns>
    private static InstallLicenceCommandHandler HandlerOver(CoverageHarness harness)
    {
        var options = new Microsoft.Extensions.Options.OptionsWrapper<LicenceStorageOptions>(
            new LicenceStorageOptions { LicenceFilePath = harness.LicenceFilePath });
        var verifier = new LicenceVerifier(
            new Ed25519LicenceSignatureVerifier(TestKeys.TrustedKey),
            harness.Clock,
            new FixedServerIdentitySource());

        return new InstallLicenceCommandHandler(
            harness.RawText,
            verifier,
            new FileLicenceWriter(options),
            new LicenceInstallLock(),
            LicensingTestContext.StatusText(),
            new LicensingAuditJournal(new RecordingAuditWriter(), new FakeCurrentUser(isAdmin: true)),
            harness.Source,
            harness.Cache);
    }

    /// <summary>Installing a licence is seen by the window's very next answer, with no waiting for the remembered one to age out.</summary>
    [Fact]
    public async Task A_successful_install_is_seen_by_the_next_question_at_once()
    {
        using var harness = new CoverageHarness(ValidLeaseSource());
        var handler = HandlerOver(harness);
        var before = await harness.Window.GetAsync("mail", CancellationToken.None);

        var result = await handler.HandleAsync(
            new InstallLicenceCommand(LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, modules: ["mail"])),
            CancellationToken.None);
        var after = await harness.Window.GetAsync("mail", CancellationToken.None);

        Assert.Equal(EntitlementVerdict.NotCovered, before.Verdict);
        Assert.True(result.IsSuccess);
        Assert.Equal(EntitlementVerdict.Covered, after.Verdict);
    }

    /// <summary>A refused install does not disturb the verdict already remembered; the success above is its control.</summary>
    [Fact]
    public async Task A_refused_install_leaves_the_remembered_verdict_standing()
    {
        using var harness = new CoverageHarness(ValidLeaseSource());
        var handler = HandlerOver(harness);
        await handler.HandleAsync(
            new InstallLicenceCommand(LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, modules: ["mail"])),
            CancellationToken.None);
        Assert.Equal(EntitlementVerdict.Covered, (await harness.Window.GetAsync("mail", CancellationToken.None)).Verdict);

        var refused = await handler.HandleAsync(new InstallLicenceCommand("garbage"), CancellationToken.None);

        Assert.False(refused.IsSuccess);
        Assert.Equal(EntitlementVerdict.Covered, (await harness.Window.GetAsync("mail", CancellationToken.None)).Verdict);
    }

    /// <summary>The install answer carries the lease's standing, so the screen can say the lease is fine or missing.</summary>
    [Fact]
    public async Task The_install_answer_reports_the_standing_of_the_lease()
    {
        using var withLease = new CoverageHarness(ValidLeaseSource());
        using var withoutLease = new CoverageHarness(new CountingLeaseSource(LeaseReading.Absent.Instance));
        var command = new InstallLicenceCommand(LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, modules: ["mail"]));

        var fresh = await HandlerOver(withLease).HandleAsync(command, CancellationToken.None);
        var missing = await HandlerOver(withoutLease).HandleAsync(command, CancellationToken.None);

        Assert.Equal(LeaseStandingKind.Fresh, fresh.Value!.Lease);
        Assert.Equal(CoverageHarness.Start.AddDays(14), fresh.Value.LeaseValidUntil);
        Assert.Equal(LeaseStandingKind.Missing, missing.Value!.Lease);
    }
}
