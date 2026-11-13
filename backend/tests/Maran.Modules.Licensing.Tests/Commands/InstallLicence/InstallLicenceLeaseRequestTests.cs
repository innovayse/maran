using Maran.Modules.Licensing.Commands.InstallLicence;
using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Interfaces;
using Maran.Modules.Licensing.Options;
using Maran.Modules.Licensing.Services;
using Maran.Modules.Licensing.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Licensing.Tests.Commands.InstallLicence;

/// <summary>
/// Covers that installing a licence asks for its lease at once (issue #537): once, only after a licence was
/// installed, from outside the install lock, and without ever turning a good install into a failed one. The answer
/// carries the server's id (issue #536) beside the lease request's outcome.
/// </summary>
public sealed class InstallLicenceLeaseRequestTests
{
    /// <summary>A machine id as the marketplace spells one.</summary>
    private const string ServerId = "0123456789abcdef0123456789abcdef";

    /// <summary>Builds the install handler over a harness and the given requester.</summary>
    /// <param name="harness">The harness whose licence file, source and cache the handler uses.</param>
    /// <param name="requester">The requester under observation.</param>
    /// <param name="installLock">The install lock the handler runs under.</param>
    /// <returns>The handler.</returns>
    private static InstallLicenceCommandHandler HandlerOver(
        CoverageHarness harness,
        ILeaseRenewalRequester requester,
        LicenceInstallLock installLock)
    {
        var options = new Microsoft.Extensions.Options.OptionsWrapper<LicenceStorageOptions>(
            new LicenceStorageOptions { LicenceFilePath = harness.LicenceFilePath });
        var verifier = new LicenceVerifier(
            new Ed25519LicenceSignatureVerifier(TestKeys.TrustedKey),
            harness.Clock,
            new FixedServerIdentitySource(ServerId));

        return new InstallLicenceCommandHandler(
            harness.RawText,
            verifier,
            new FileLicenceWriter(options),
            installLock,
            LicensingTestContext.StatusText(),
            new LicensingAuditJournal(new RecordingAuditWriter(), new FakeCurrentUser(isAdmin: true)),
            harness.Source,
            requester,
            LicensingTestContext.ServerFacts(ServerId),
            harness.Cache,
            LicensingTestContext.ExpiryWarnings(harness.Clock));
    }

    /// <summary>A command carrying a valid licence.</summary>
    /// <returns>The command.</returns>
    private static InstallLicenceCommand ValidCommand()
    {
        return new InstallLicenceCommand(LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, modules: ["mail"]));
    }

    /// <summary>A successful install asks for a lease exactly once and the answer says what that came to.</summary>
    [Fact]
    public async Task A_successful_install_requests_a_lease_once_and_reports_the_outcome()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(LeaseReading.Absent.Instance));
        var requester = new RecordingLeaseRenewalRequester(LeaseRenewalOutcome.Unreachable);

        var result = await HandlerOver(harness, requester, new LicenceInstallLock())
            .HandleAsync(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, requester.Requests);
        Assert.Equal(["lic-001"], requester.LicenceIds);
        Assert.Equal(LeaseRenewalOutcome.Unreachable, result.Value!.LeaseRequest);
        Assert.Contains("try again", result.Value.LeaseRequestDisplayName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A refused upload asks for no lease: there is no new licence to lease. The success above is its control.</summary>
    [Fact]
    public async Task A_refused_install_requests_no_lease()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(LeaseReading.Absent.Instance));
        var requester = new RecordingLeaseRenewalRequester();

        var result = await HandlerOver(harness, requester, new LicenceInstallLock())
            .HandleAsync(new InstallLicenceCommand("this is not a licence"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, requester.Requests);
    }

    /// <summary>The request is made after the install lock is released, because storing a lease takes the same lock.</summary>
    [Fact]
    public async Task The_request_is_made_after_the_install_lock_is_released()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(LeaseReading.Absent.Instance));
        var installLock = new LicenceInstallLock();
        var requester = new RecordingLeaseRenewalRequester(installLock: installLock);

        await HandlerOver(harness, requester, installLock).HandleAsync(ValidCommand(), CancellationToken.None);

        Assert.Equal(1, requester.Requests);
        Assert.True(requester.LockWasFree);
    }

    /// <summary>The answer carries the server's id and the claim link beside the licence, with a request that came to nothing to report.</summary>
    [Fact]
    public async Task The_answer_names_the_server_and_links_to_the_claim_page()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(LeaseReading.Absent.Instance));

        var result = await HandlerOver(harness, new RecordingLeaseRenewalRequester(), new LicenceInstallLock())
            .HandleAsync(ValidCommand(), CancellationToken.None);

        Assert.Equal(ServerId, result.Value!.ServerId);
        Assert.Equal("https://plugins-maran.innovayse.com/claim?server=" + ServerId, result.Value.ClaimUrl);
    }
}
