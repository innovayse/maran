using System.Globalization;
using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Models;
using Maran.Modules.Licensing.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Licensing.Tests.Services;

/// <summary>
/// Covers <see cref="Maran.Modules.Licensing.Services.LeaseRenewalWorker"/>: what each pass writes to the audit
/// journal, when the operator is alerted and when told it is over. The pass itself is driven directly, over the
/// real renewal, the real shared alert path and a journal that can be taken down; no timer runs.
/// </summary>
public sealed class LeaseRenewalWorkerTests
{
    /// <summary>The instant the world starts at.</summary>
    private static readonly DateTimeOffset Start = RenewalHarness.Start;

    /// <summary>Makes the world's licence service fail as an unreachable host would.</summary>
    /// <param name="world">The world.</param>
    private static void ServiceDown(RenewalWorld world)
    {
        world.Harness.Client.Answer = LeaseServiceAnswer.Without(LeaseServiceAnswerKind.Unreachable);
    }

    /// <summary>Runs one pass.</summary>
    /// <param name="world">The world.</param>
    /// <returns>What the pass did.</returns>
    private static async Task<LeaseRenewalResult> PassAsync(RenewalWorld world)
    {
        return await world.Worker.RunPassAsync(CancellationToken.None);
    }

    /// <summary>The alerts published so far.</summary>
    /// <param name="world">The world.</param>
    /// <returns>The alerts, in order.</returns>
    private static List<OperatorAlert> Alerts(RenewalWorld world)
    {
        return world.Bus.Published.OfType<OperatorAlertRaised>().Select(raised => { return raised.Alert; }).ToList();
    }

    /// <summary>A stored lease is journalled as the panel with the lease's end and nothing that identifies the licence or the server.</summary>
    [Fact]
    public async Task A_stored_lease_is_journalled_as_the_panel_with_its_end_and_no_identity()
    {
        using var world = new RenewalWorld();
        world.Harness.InstallLicence(RenewalHarness.Licence());
        world.Harness.Client.Returns(RenewalHarness.Lease(Start));

        var result = await PassAsync(world);

        Assert.Equal(LeaseRenewalOutcome.Renewed, result.Outcome);
        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(LicenceAuditActions.LeaseRenewed, entry.Action);
        Assert.Equal(
            "outcome=Renewed;validUntil=" + Start.AddDays(14).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            entry.Subject);
        Assert.Equal(SystemAuditEntry.NameFor("licensing"), entry.ActorUsername);
        Assert.Null(entry.ActorUserId);
        Assert.True(entry.Succeeded);
        Assert.DoesNotContain("lic-001", entry.Subject, StringComparison.Ordinal);
        Assert.Empty(Alerts(world));
    }

    /// <summary>A pass with nothing to do writes nothing and says nothing; a pass with something to do is the control above.</summary>
    [Fact]
    public async Task A_pass_with_nothing_to_do_writes_nothing_and_alerts_nobody()
    {
        using var world = new RenewalWorld();

        var result = await PassAsync(world);

        Assert.Equal(LeaseRenewalOutcome.SkippedNoLicence, result.Outcome);
        Assert.Empty(world.Audit.Entries);
        Assert.Empty(world.Bus.Published);
    }

    /// <summary>Each failed pass is journalled as a refused entry carrying the length of the run, and one failure alerts nobody.</summary>
    [Fact]
    public async Task A_failed_pass_is_journalled_with_the_length_of_the_run_and_two_failures_alert_nobody()
    {
        using var world = new RenewalWorld();
        world.Harness.InstallLicence(RenewalHarness.Licence());
        ServiceDown(world);

        await PassAsync(world);
        await PassAsync(world);

        Assert.Equal(
            ["outcome=Unreachable;consecutiveFailures=1", "outcome=Unreachable;consecutiveFailures=2"],
            world.Audit.Entries.Select(entry => { return entry.Subject; }));
        Assert.All(world.Audit.Entries, entry =>
        {
            Assert.Equal(LicenceAuditActions.LeaseRenewalFailed, entry.Action);
            Assert.False(entry.Succeeded);
        });
        Assert.Empty(Alerts(world));
    }

    /// <summary>The third failure in a row raises ONE alert through the shared path; the fourth raises nothing more.</summary>
    [Fact]
    public async Task The_third_failure_in_a_row_raises_one_alert_and_the_fourth_does_not_repeat_it()
    {
        using var world = new RenewalWorld();
        world.Harness.InstallLicence(RenewalHarness.Licence());
        ServiceDown(world);

        await PassAsync(world);
        await PassAsync(world);
        await PassAsync(world);

        var alert = Assert.Single(Alerts(world));
        Assert.Equal(AlertSource.Licensing, alert.Source);
        Assert.Equal("LeaseRenewalFailed", alert.Code);
        Assert.Equal(AlertTransition.Raised, alert.Transition);
        Assert.Equal("licence lease", alert.Subject);
        Assert.Equal("outcome=Unreachable consecutiveFailures=3", alert.Detail);
        var mail = Assert.Single(world.Bus.Published.OfType<SendMailRequested>());
        Assert.Equal(RenewalWorld.OperatorAddress, mail.Recipient);
        Assert.Equal(MailPurpose.OperatorAlert, mail.Purpose);
        Assert.Contains("3", mail.Body, StringComparison.Ordinal);
        Assert.Single(world.Bus.Published.OfType<PanelNotificationRaised>());
        Assert.Contains(world.Audit.Entries, entry => { return entry.Action == MonitoringAuditActions.AlertRaised; });

        await PassAsync(world);

        Assert.Single(Alerts(world));
    }

    /// <summary>A lease stored after the alert resolves it once; a stored lease with no alert raised resolves nothing.</summary>
    [Fact]
    public async Task A_stored_lease_after_the_alert_resolves_it_once()
    {
        using var world = new RenewalWorld();
        world.Harness.InstallLicence(RenewalHarness.Licence());
        ServiceDown(world);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await PassAsync(world);
        }

        world.Harness.Client.Returns(RenewalHarness.Lease(Start));
        var renewed = await PassAsync(world);
        await PassAsync(world);

        Assert.Equal(LeaseRenewalOutcome.Renewed, renewed.Outcome);
        Assert.Equal([AlertTransition.Raised, AlertTransition.Resolved], Alerts(world).Select(alert => { return alert.Transition; }));
        Assert.Contains(world.Audit.Entries, entry => { return entry.Action == MonitoringAuditActions.AlertResolved; });
        Assert.Equal(2, Alerts(world).Count);
    }

    /// <summary>A lease stored with no alert raised resolves nothing: the control for the resolution above.</summary>
    [Fact]
    public async Task A_stored_lease_with_no_alert_raised_resolves_nothing()
    {
        using var world = new RenewalWorld();
        world.Harness.InstallLicence(RenewalHarness.Licence());
        ServiceDown(world);
        await PassAsync(world);
        await PassAsync(world);

        world.Harness.Client.Returns(RenewalHarness.Lease(Start));
        await PassAsync(world);

        Assert.Empty(Alerts(world));
    }

    /// <summary>A stored lease breaks a run of failures: two, a success and two more never alert.</summary>
    [Fact]
    public async Task A_stored_lease_breaks_the_run_of_failures()
    {
        using var world = new RenewalWorld();
        world.Harness.InstallLicence(RenewalHarness.Licence());
        ServiceDown(world);
        await PassAsync(world);
        await PassAsync(world);

        world.Harness.Client.Returns(RenewalHarness.Lease(Start));
        await PassAsync(world);
        world.Harness.Clock.Advance(TimeSpan.FromHours(21));
        ServiceDown(world);
        await PassAsync(world);
        await PassAsync(world);

        Assert.Empty(Alerts(world));
        Assert.Equal(
            "outcome=Unreachable;consecutiveFailures=2",
            world.Audit.Entries.Last(entry => { return entry.Action == LicenceAuditActions.LeaseRenewalFailed; }).Subject);
    }

    /// <summary>A pass with nothing to do neither extends nor ends a run of failures.</summary>
    [Fact]
    public async Task A_pass_with_nothing_to_do_neither_extends_nor_ends_a_run_of_failures()
    {
        using var world = new RenewalWorld();
        world.Harness.InstallLicence(RenewalHarness.Licence());
        ServiceDown(world);
        await PassAsync(world);
        await PassAsync(world);

        world.Harness.InstallLicence(RenewalHarness.Licence(offline: true));
        Assert.Equal(LeaseRenewalOutcome.SkippedOffline, (await PassAsync(world)).Outcome);
        Assert.Empty(Alerts(world));

        world.Harness.InstallLicence(RenewalHarness.Licence());
        await PassAsync(world);

        Assert.Single(Alerts(world));
    }

    /// <summary>Every failure kind counts toward the run, not only an unreachable service.</summary>
    [Fact]
    public async Task Every_failure_kind_counts_toward_the_run()
    {
        using var world = new RenewalWorld();
        world.Harness.InstallLicence(RenewalHarness.Licence());

        world.Harness.Client.Answer = LeaseServiceAnswer.Without(LeaseServiceAnswerKind.Refused);
        await PassAsync(world);
        world.Harness.Client.Answer = LeaseServiceAnswer.Without(LeaseServiceAnswerKind.TooLarge);
        await PassAsync(world);
        world.Harness.Client.Returns("not json");
        await PassAsync(world);

        var alert = Assert.Single(Alerts(world));
        Assert.Equal("outcome=AnswerRejected consecutiveFailures=3", alert.Detail);
    }

    /// <summary>A journal that is down does not stop the pass, and the alert it owed is raised once the journal is back.</summary>
    [Fact]
    public async Task A_journal_that_is_down_does_not_stop_the_pass_and_the_alert_is_raised_when_it_is_back()
    {
        using var world = new RenewalWorld();
        world.Harness.InstallLicence(RenewalHarness.Licence());
        ServiceDown(world);
        world.Audit.Down = true;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.Equal(LeaseRenewalOutcome.Unreachable, (await PassAsync(world)).Outcome);
        }

        Assert.Empty(world.Audit.Entries);
        Assert.Empty(Alerts(world));

        world.Audit.Down = false;
        await PassAsync(world);

        var alert = Assert.Single(Alerts(world));
        Assert.Equal("outcome=Unreachable consecutiveFailures=4", alert.Detail);
    }

    /// <summary>A pass that is cancelled is cancelled; it is not counted as a failure.</summary>
    [Fact]
    public async Task A_cancelled_pass_is_not_counted_as_a_failure()
    {
        using var world = new RenewalWorld();
        world.Harness.InstallLicence(RenewalHarness.Licence());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => { return world.Worker.RunPassAsync(cancelled.Token); });

        Assert.Empty(world.Audit.Entries);
    }
}
