using Maran.Modules.Licensing.Commands.InstallLicence;
using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Options;
using Maran.Modules.Licensing.Services;
using Maran.Modules.Licensing.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Licensing.Tests.Commands.InstallLicence;

/// <summary>
/// Covers installing a licence over the REAL renewal (issue #537): the real worker, renewer, verifier, reader and
/// file writer on a temporary directory, with only the licence service scripted. Installing makes a lease appear
/// without waiting for a schedule, an install that changes nothing asks for nothing, and a service that is down
/// does not stop the licence from being installed.
/// </summary>
public sealed class InstallLicenceRealRenewalTests
{
    /// <summary>Builds the install handler over the world's own renewal, sharing its install lock.</summary>
    /// <param name="world">The world whose worker, lock, sources and clock the handler uses.</param>
    /// <returns>The handler.</returns>
    private static InstallLicenceCommandHandler HandlerOver(RenewalWorld world)
    {
        var harness = world.Harness;
        var options = new Microsoft.Extensions.Options.OptionsWrapper<LicenceStorageOptions>(
            new LicenceStorageOptions { LicenceFilePath = harness.LicenceFilePath });
        var identity = new FixedServerIdentitySource();
        var verifier = new LicenceVerifier(new Ed25519LicenceSignatureVerifier(TestKeys.TrustedKey), harness.Clock, identity);

        return new InstallLicenceCommandHandler(
            harness.RawText,
            verifier,
            new FileLicenceWriter(options),
            harness.InstallLock,
            LicensingTestContext.StatusText(),
            new LicensingAuditJournal(new RecordingAuditWriter(), new FakeCurrentUser(isAdmin: true)),
            new LicenceCoverageSource(harness.RawText, verifier, harness.Source, identity, harness.Clock),
            world.Worker,
            LicensingTestContext.ServerFacts(),
            harness.Invalidator,
            LicensingTestContext.ExpiryWarnings(harness.Clock));
    }

    /// <summary>Installing a licence stores its lease then and there, and the answer shows the lease as current.</summary>
    [Fact]
    public async Task Installing_a_licence_stores_its_lease_at_once()
    {
        using var world = new RenewalWorld();
        world.Harness.Client.Returns(RenewalHarness.Lease(RenewalHarness.Start));
        Assert.Empty(world.Harness.Client.Requests);
        Assert.Null(world.Harness.StoredLease);

        var result = await HandlerOver(world).HandleAsync(
            new InstallLicenceCommand(RenewalHarness.Licence()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["lic-001"], world.Harness.Client.Requests);
        Assert.NotNull(world.Harness.StoredLease);
        Assert.Equal(LeaseRenewalOutcome.Renewed, result.Value!.LeaseRequest);
        Assert.Equal(LeaseStandingKind.Fresh, result.Value.Lease);
        Assert.Equal(RenewalHarness.Start.AddDays(14), result.Value.LeaseValidUntil);
    }

    /// <summary>A licence service that cannot be reached leaves the licence installed, no lease, and an answer that says why.</summary>
    [Fact]
    public async Task A_service_that_is_down_does_not_stop_the_licence_being_installed()
    {
        using var world = new RenewalWorld();

        var result = await HandlerOver(world).HandleAsync(
            new InstallLicenceCommand(RenewalHarness.Licence()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(world.Harness.LicenceFilePath));
        Assert.Null(world.Harness.StoredLease);
        Assert.Equal(LeaseRenewalOutcome.Unreachable, result.Value!.LeaseRequest);
        Assert.Equal(LeaseStandingKind.Missing, result.Value.Lease);
    }

    /// <summary>A request that fails counts in the same run of failures the schedule keeps and writes the same journal entry.</summary>
    [Fact]
    public async Task A_failed_request_is_journalled_like_a_failed_scheduled_pass()
    {
        using var world = new RenewalWorld();

        await HandlerOver(world).HandleAsync(new InstallLicenceCommand(RenewalHarness.Licence()), CancellationToken.None);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(LicenceAuditActions.LeaseRenewalFailed, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>An offline licence needs no lease, so nothing is asked of the service.</summary>
    [Fact]
    public async Task An_offline_licence_asks_the_service_for_nothing()
    {
        using var world = new RenewalWorld();

        var result = await HandlerOver(world).HandleAsync(
            new InstallLicenceCommand(RenewalHarness.Licence(offline: true)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(world.Harness.Client.Requests);
        Assert.Equal(LeaseRenewalOutcome.SkippedOffline, result.Value!.LeaseRequest);
        Assert.Equal(LeaseStandingKind.NotRequired, result.Value.Lease);
    }

    /// <summary>Replacing a licence asks for the new licence's lease even though the old licence's lease is recent.</summary>
    [Fact]
    public async Task Replacing_a_licence_asks_for_the_new_licences_lease()
    {
        using var world = new RenewalWorld();
        world.Harness.InstallLicence(RenewalHarness.Licence("lic-001"));
        world.Harness.StoreLease(RenewalHarness.Lease(RenewalHarness.Start, licence: "lic-001"));
        world.Harness.Client.Returns(RenewalHarness.Lease(RenewalHarness.Start, licence: "lic-002"));

        var result = await HandlerOver(world).HandleAsync(
            new InstallLicenceCommand(RenewalHarness.Licence("lic-002")),
            CancellationToken.None);

        Assert.Equal(["lic-002"], world.Harness.Client.Requests);
        Assert.Equal(LeaseRenewalOutcome.Renewed, result.Value!.LeaseRequest);
        Assert.Equal(LeaseStandingKind.Fresh, result.Value.Lease);
    }

    /// <summary>
    /// Installing the licence that is already installed, with its lease recent, asks for nothing: the request goes
    /// through the renewal's own rule of when a lease is due, not around it. Replacing above is the control.
    /// </summary>
    [Fact]
    public async Task Installing_the_same_licence_again_with_a_recent_lease_asks_for_nothing()
    {
        using var world = new RenewalWorld();
        world.Harness.InstallLicence(RenewalHarness.Licence("lic-001"));
        world.Harness.StoreLease(RenewalHarness.Lease(RenewalHarness.Start, licence: "lic-001"));

        var result = await HandlerOver(world).HandleAsync(
            new InstallLicenceCommand(RenewalHarness.Licence("lic-001")),
            CancellationToken.None);

        Assert.Empty(world.Harness.Client.Requests);
        Assert.Equal(LeaseRenewalOutcome.NotDue, result.Value!.LeaseRequest);
        Assert.Equal(LeaseStandingKind.Fresh, result.Value.Lease);
    }

    /// <summary>
    /// When a second install replaces the licence while the first one's lease is being requested, the first answer
    /// still describes the first licence: its own id, a request that came to nothing because the licence changed, and
    /// the stored lease of the other licence standing as such, not as the first licence's own.
    /// </summary>
    [Fact]
    public async Task An_answer_describes_its_own_licence_when_another_install_replaced_it_meanwhile()
    {
        using var world = new RenewalWorld();
        world.Harness.Client.Returns(RenewalHarness.Lease(RenewalHarness.Start, licence: "lic-001"));
        world.Harness.Client.WhileAsking = () =>
        {
            world.Harness.InstallLicence(RenewalHarness.Licence("lic-002"));
            world.Harness.StoreLease(RenewalHarness.Lease(RenewalHarness.Start, licence: "lic-002"));
        };

        var result = await HandlerOver(world).HandleAsync(
            new InstallLicenceCommand(RenewalHarness.Licence("lic-001")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("lic-001", result.Value!.LicenceId);
        Assert.Equal(LeaseRenewalOutcome.LicenceChanged, result.Value.LeaseRequest);
        Assert.Equal(LeaseStandingKind.WrongLicence, result.Value.Lease);
    }
}
