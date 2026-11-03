using System.Globalization;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Backups.Jobs;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The closing criterion of issue #134, second and third legs: a missed scheduled run and a
/// destination the server cannot use each produce their notification — the administrator's mail
/// request and an <c>AlertRaised</c> journal entry — over the real sweep and real PostgreSQL, with
/// only the agent stubbed.
/// </summary>
/// <remarks>
/// <para>
/// <b>The clock is the test's.</b> The sweep is driven past the named hour by moving the injected
/// clock, never by waiting, so "thirty minutes late" costs nothing and is exact. A schedule enabled
/// at the clock's current instant names the next whole hour, so the same seed gives a run that is
/// ten minutes late (inside the tolerance, the control) and one that is forty-five (outside it).
/// </para>
/// <para>
/// <b>What "an unreachable target" can be today.</b> The agent serves only the local destination, so
/// a remote target refusing writes after it was accepted cannot be observed until remote
/// destinations land (issue #160) and is documented as that limit, not faked here. What the panel
/// CAN observe, and what these tests prove, is a destination it cannot use: a remote kind this agent
/// does not serve, and no destination configured at all. A destination deleted out from under a
/// schedule is not a third case — the database refuses the delete while a schedule names it.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class BackupVerificationScheduleAlertTests : IAsyncLifetime
{
    /// <summary>The database this test class owns on the shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public BackupVerificationScheduleAlertTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Prepares the fixture before the tests run.</summary>
    /// <returns>The task that creates this class's database.</returns>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    /// <returns>A completed task; the shared server outlives this class.</returns>
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>A run that starts forty-five minutes after its named hour is journalled and mailed as missed.</summary>
    [Fact]
    public async Task A_run_started_forty_five_minutes_late_is_journalled_and_mailed_as_missed()
    {
        var agent = new ScriptedVerificationAgentBackupClient();
        var mailer = new RecordingMailer(TimeSpan.Zero);
        var clock = new AdvancingClock();
        await using var factory = await BackupVerificationWorld.BootAsync(
            _pg, agent, mailer, alertRecipient: true, clock);

        var (scheduleId, dueAt) = await SeedScheduleAsync(factory, clock);
        clock.Advance(dueAt - clock.UtcNow + TimeSpan.FromMinutes(45));

        Assert.Equal(1, await SweepAsync(factory));

        var alert = Assert.Single(await BackupVerificationWorld.AuditEntriesAsync(
            factory, MonitoringAuditActions.AlertRaised));
        Assert.Equal($"ScheduledRunMissed:{scheduleId}", alert.Subject);
        Assert.True(alert.Succeeded);

        var sent = Assert.Single(await BackupVerificationWorld.WaitForMailAsync(mailer, 1));
        Assert.Equal(FixedAlertRecipientDirectory.Address, sent.Recipient);
        var namedHour = dueAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        Assert.Contains(namedHour, sent.Body, StringComparison.Ordinal);
        Assert.Contains("acme", sent.Body, StringComparison.Ordinal);

        // The late run still happened: a missed hour is reported, not skipped.
        Assert.Single(agent.Creates);
    }

    /// <summary>A run that starts ten minutes after its named hour is on time and raises no missed-run alert.</summary>
    /// <remarks>
    /// The inverse control of the test above: the same seed, the same sweep, and the only thing
    /// changed is how late the clock is. A sweep that alerted on every run would pass the missed test
    /// and fail here.
    /// </remarks>
    [Fact]
    public async Task A_run_started_ten_minutes_late_raises_no_missed_run_alert()
    {
        var agent = new ScriptedVerificationAgentBackupClient();
        var mailer = new RecordingMailer(TimeSpan.Zero);
        var clock = new AdvancingClock();
        await using var factory = await BackupVerificationWorld.BootAsync(
            _pg, agent, mailer, alertRecipient: true, clock);

        var (_, dueAt) = await SeedScheduleAsync(factory, clock);
        clock.Advance(dueAt - clock.UtcNow + TimeSpan.FromMinutes(10));

        Assert.Equal(1, await SweepAsync(factory));

        Assert.Empty(await BackupVerificationWorld.AuditEntriesAsync(factory, MonitoringAuditActions.AlertRaised));
        Assert.Empty(await BackupVerificationWorld.DrainedMailAsync(factory, mailer));
        Assert.Single(agent.Creates);
    }

    /// <summary>A destination of a kind the agent does not serve is journalled and mailed, and starts no run.</summary>
    [Fact]
    public async Task A_destination_the_agent_does_not_serve_is_journalled_and_mailed_and_starts_no_run()
    {
        var agent = new ScriptedVerificationAgentBackupClient();
        var mailer = new RecordingMailer(TimeSpan.Zero);
        var clock = new AdvancingClock();
        await using var factory = await BackupVerificationWorld.BootAsync(
            _pg, agent, mailer, alertRecipient: true, clock);

        var remote = await BackupVerificationWorld.SeedRemoteDestinationAsync(factory);
        var (_, dueAt) = await SeedScheduleAsync(factory, clock, remote);
        clock.Advance(dueAt - clock.UtcNow + TimeSpan.FromMinutes(10));

        Assert.Equal(0, await SweepAsync(factory));

        await AssertRefusedAsync(factory, agent, mailer);
    }

    /// <summary>A server with no destination configured journals and mails the refusal, and starts no run.</summary>
    [Fact]
    public async Task A_server_with_no_destination_configured_journals_and_mails_the_refusal_and_starts_no_run()
    {
        var agent = new ScriptedVerificationAgentBackupClient();
        var mailer = new RecordingMailer(TimeSpan.Zero);
        var clock = new AdvancingClock();
        await using var factory = await BackupVerificationWorld.BootAsync(
            _pg, agent, mailer, alertRecipient: true, clock);

        var (_, dueAt) = await SeedScheduleAsync(factory, clock);
        await BackupVerificationWorld.RemoveEveryDestinationAsync(factory);
        clock.Advance(dueAt - clock.UtcNow + TimeSpan.FromMinutes(10));

        Assert.Equal(0, await SweepAsync(factory));

        await AssertRefusedAsync(factory, agent, mailer);
    }

    /// <summary>With no alert address configured a refusal is journalled and its mail skipped.</summary>
    [Fact]
    public async Task With_no_alert_address_a_refused_destination_is_journalled_and_its_mail_skipped()
    {
        var agent = new ScriptedVerificationAgentBackupClient();
        var mailer = new RecordingMailer(TimeSpan.Zero);
        var clock = new AdvancingClock();
        await using var factory = await BackupVerificationWorld.BootAsync(
            _pg, agent, mailer, alertRecipient: false, clock);

        var remote = await BackupVerificationWorld.SeedRemoteDestinationAsync(factory);
        var (_, dueAt) = await SeedScheduleAsync(factory, clock, remote);
        clock.Advance(dueAt - clock.UtcNow + TimeSpan.FromMinutes(10));

        Assert.Equal(0, await SweepAsync(factory));

        var alert = Assert.Single(await BackupVerificationWorld.AuditEntriesAsync(
            factory, MonitoringAuditActions.AlertRaised));
        Assert.Equal("DestinationRefused:acme", alert.Subject);

        var skipped = Assert.Single(await BackupVerificationWorld.AuditEntriesAsync(
            factory, MailAuditActions.MailSkippedNoSmtp));
        Assert.Equal(alert.Subject, skipped.Subject);
        Assert.True(skipped.Succeeded);
        Assert.Empty(await BackupVerificationWorld.DrainedMailAsync(factory, mailer));
    }

    /// <summary>Asserts a refusal produced its alert and mail and that no run was started.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="agent">The scripted agent, which must never have been asked to create.</param>
    /// <param name="mailer">The mail server's double.</param>
    /// <returns>The task that checks the journal, the mail, the agent and the rows.</returns>
    private static async Task AssertRefusedAsync(
        WebApplicationFactory<Program> factory,
        ScriptedVerificationAgentBackupClient agent,
        RecordingMailer mailer)
    {
        var alert = Assert.Single(await BackupVerificationWorld.AuditEntriesAsync(
            factory, MonitoringAuditActions.AlertRaised));
        Assert.Equal("DestinationRefused:acme", alert.Subject);
        Assert.True(alert.Succeeded);

        var sent = Assert.Single(await BackupVerificationWorld.WaitForMailAsync(mailer, 1));
        Assert.Equal(FixedAlertRecipientDirectory.Address, sent.Recipient);
        Assert.Contains("acme", sent.Subject, StringComparison.Ordinal);

        Assert.Empty(agent.Creates);
        Assert.Equal(0, agent.VerificationCount);
        Assert.Empty(await BackupVerificationWorld.BackupsAsync(factory));
    }

    /// <summary>Stores a schedule for <c>acme</c> enabled at the clock's current instant.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="clock">The clock the host reads.</param>
    /// <param name="destinationId">The destination the schedule names, or <c>null</c> for the default.</param>
    /// <returns>The schedule's identifier and the next whole hour it names.</returns>
    private static async Task<(Guid ScheduleId, DateTimeOffset DueAt)> SeedScheduleAsync(
        WebApplicationFactory<Program> factory,
        AdvancingClock clock,
        Guid? destinationId = null)
    {
        var start = clock.UtcNow;
        var dueAt = new DateTimeOffset(start.Year, start.Month, start.Day, start.Hour, 0, 0, TimeSpan.Zero)
            .AddHours(1);

        var scheduleId = await BackupVerificationWorld.SeedDailyScheduleAsync(
            factory, "acme", destinationId, dueAt.Hour, start);

        return (scheduleId, dueAt);
    }

    /// <summary>Runs the scheduled sweep once, as the panel's timer does.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>How many account backups the sweep started.</returns>
    private static async Task<int> SweepAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<BackupRunHandler>();

        return await handler.HandleAsync(new BackupRunRequested(), CancellationToken.None);
    }
}
