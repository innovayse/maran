using System.Globalization;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Jobs;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The closing criterion of issue #134, first leg: a corrupted artifact produces a notification —
/// the administrator's mail request and an <c>AlertRaised</c> journal entry — and a sound one
/// produces none, over real HTTP, real PostgreSQL and the real sweep, with only the agent stubbed.
/// </summary>
/// <remarks>
/// <para>
/// <b>What the stub states, and what it cannot.</b> The agent answers "the artifact does not match
/// its record" after a run that completed. Whether the real agent finds a flipped byte is the agent's
/// own tests' and the polygon suite's to prove; what is observed HERE is the panel's reaction to
/// that finding, which no unit test sees end to end (the stored state, the localized wording on the
/// wire, the journal and the mail request that follow).
/// </para>
/// <para>
/// <b>Each negative has its positive beside it.</b> "A sound artifact mails nothing" is read against
/// the corrupted artifact in the same class that does, and every no-mail assertion is made on the
/// journal first (written before the request is published, so it cannot race) and on the mailer
/// second, after a marker proved the mail path was alive.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class BackupVerificationAlertTests : IAsyncLifetime
{
    /// <summary>The kind prefix the journal subject of a failed-verification alert starts with.</summary>
    private const string AlertSubjectPrefix = "VerificationFailed:";

    /// <summary>The database this test class owns on the shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public BackupVerificationAlertTests(PostgresFixture postgres)
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

    /// <summary>A corrupted artifact after a scheduled run is recorded, journalled and mailed.</summary>
    /// <remarks>
    /// The sweep is driven past the named hour by ten minutes, which is inside the thirty the panel
    /// tolerates, so the only alert this run can raise is the verification one — a missed-run alert
    /// here would mean the test measured the wrong thing.
    /// </remarks>
    [Fact]
    public async Task A_corrupted_artifact_after_a_scheduled_run_is_recorded_journalled_and_mailed()
    {
        var agent = new ScriptedVerificationAgentBackupClient();
        var mailer = new RecordingMailer(TimeSpan.Zero);
        var clock = new AdvancingClock();
        await using var factory = await BackupVerificationWorld.BootAsync(
            _pg, agent, mailer, alertRecipient: true, clock);
        agent.AnswerFailure("AgentValidationFailed", ErrorType.Validation);

        await RunSweepTenMinutesLateAsync(factory, clock);

        var backup = Assert.Single(await BackupVerificationWorld.BackupsAsync(factory));
        Assert.Equal(BackupKind.Scheduled, backup.Kind);
        Assert.Equal(BackupVerificationState.Failed, backup.VerificationState);
        Assert.False(string.IsNullOrWhiteSpace(backup.VerificationFailureCode));

        var alert = Assert.Single(await AlertsAsync(factory));
        Assert.True(alert.Succeeded);
        Assert.Equal($"{AlertSubjectPrefix}{backup.Id}", alert.Subject);

        var sent = Assert.Single(await BackupVerificationWorld.WaitForMailAsync(mailer, 1));
        Assert.Equal(FixedAlertRecipientDirectory.Address, sent.Recipient);
        AssertMailNamesAccountAndRun(sent, backup);
    }

    /// <summary>A corrupted artifact after a manual run is mailed too and leaves the run completed.</summary>
    /// <remarks>
    /// A manual run is watched by whoever asked for it, but its verification finishes after they have
    /// stopped watching, so the finding is still unattended. The run row keeps its own outcome: the
    /// run produced an archive, and a failed verification is a separate fact beside it.
    /// </remarks>
    [Fact]
    public async Task A_corrupted_artifact_after_a_manual_run_is_mailed_and_leaves_the_run_completed()
    {
        var agent = new ScriptedVerificationAgentBackupClient();
        var mailer = new RecordingMailer(TimeSpan.Zero);
        await using var factory = await BackupVerificationWorld.BootAsync(
            _pg, agent, mailer, alertRecipient: true);
        agent.AnswerFailure("AgentValidationFailed", ErrorType.Validation);

        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        var backupId = await BackupVerificationWorld.CreateManualBackupAsync(
            owner, await BackupVerificationWorld.AccountIdAsync(factory, "acme"));

        var row = await BackupVerificationWorld.WaitForVerificationAsync(factory, backupId);
        Assert.Equal(BackupVerificationState.Failed, row.VerificationState);
        Assert.Equal(BackupStatus.Completed, row.Status);

        var alert = Assert.Single(await BackupVerificationWorld.WaitForAuditAsync(
            factory, MonitoringAuditActions.AlertRaised, 1));
        Assert.Equal($"{AlertSubjectPrefix}{backupId}", alert.Subject);

        var sent = Assert.Single(await BackupVerificationWorld.WaitForMailAsync(mailer, 1));
        Assert.Equal(FixedAlertRecipientDirectory.Address, sent.Recipient);
        AssertMailNamesAccountAndRun(sent, row);
    }

    /// <summary>The backup row shows a failed verification with a reason in the caller's language.</summary>
    /// <remarks>
    /// The reason is the backend's to word and to translate (rules/architecture.md): the same row
    /// read in English and in Russian carries two different sentences, neither of which is the
    /// machine code, and neither of which carries anything the agent said.
    /// </remarks>
    [Fact]
    public async Task The_backup_row_shows_a_failed_verification_with_a_localized_reason()
    {
        var agent = new ScriptedVerificationAgentBackupClient();
        var mailer = new RecordingMailer(TimeSpan.Zero);
        await using var factory = await BackupVerificationWorld.BootAsync(
            _pg, agent, mailer, alertRecipient: true);
        agent.AnswerFailure("AgentValidationFailed", ErrorType.Validation);

        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        var backupId = await BackupVerificationWorld.CreateManualBackupAsync(
            owner, await BackupVerificationWorld.AccountIdAsync(factory, "acme"));
        await BackupVerificationWorld.WaitForVerificationAsync(factory, backupId);

        using var english = await BackupVerificationWorld.GetBackupAsync(owner, backupId, "en");
        using var russian = await BackupVerificationWorld.GetBackupAsync(owner, backupId, "ru");

        Assert.Equal("failed", english.RootElement.GetProperty("verificationState").GetString(), ignoreCase: true);
        Assert.Equal("completed", english.RootElement.GetProperty("status").GetString(), ignoreCase: true);
        Assert.False(string.IsNullOrWhiteSpace(english.RootElement.GetProperty("verificationStateName").GetString()));
        Assert.Equal(JsonValueKind.String, english.RootElement.GetProperty("verifiedAt").ValueKind);

        var code = english.RootElement.GetProperty("verificationFailureCode").GetString()!;
        var englishMessage = english.RootElement.GetProperty("verificationFailureDisplayName").GetString()!;
        var russianMessage = russian.RootElement.GetProperty("verificationFailureDisplayName").GetString()!;

        Assert.False(string.IsNullOrWhiteSpace(code));
        Assert.NotEqual(code, englishMessage);
        Assert.NotEqual(englishMessage, russianMessage);
    }

    /// <summary>A sound artifact is marked verified and nothing is journalled as an alert or mailed.</summary>
    /// <remarks>
    /// The positive control for every assertion above: same route, same seed, same recipient, and the
    /// only thing changed is the agent's answer. A panel that mailed on every run would pass the two
    /// tests above and fail here.
    /// </remarks>
    [Fact]
    public async Task A_sound_artifact_is_marked_verified_and_raises_no_alert()
    {
        var agent = new ScriptedVerificationAgentBackupClient();
        var mailer = new RecordingMailer(TimeSpan.Zero);
        await using var factory = await BackupVerificationWorld.BootAsync(
            _pg, agent, mailer, alertRecipient: true);
        agent.AnswerSound();

        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        var backupId = await BackupVerificationWorld.CreateManualBackupAsync(
            owner, await BackupVerificationWorld.AccountIdAsync(factory, "acme"));

        var row = await BackupVerificationWorld.WaitForVerificationAsync(factory, backupId);
        Assert.Equal(BackupVerificationState.Verified, row.VerificationState);
        Assert.NotNull(row.VerifiedAt);
        Assert.Null(row.VerificationFailureCode);

        Assert.Empty(await AlertsAsync(factory));
        Assert.Empty(await BackupVerificationWorld.DrainedMailAsync(factory, mailer));

        using var shown = await BackupVerificationWorld.GetBackupAsync(owner, backupId);
        Assert.Equal("verified", shown.RootElement.GetProperty("verificationState").GetString(), ignoreCase: true);
        Assert.Equal(JsonValueKind.Null, shown.RootElement.GetProperty("verificationFailureCode").ValueKind);
        Assert.Equal(JsonValueKind.Null, shown.RootElement.GetProperty("verificationFailureDisplayName").ValueKind);
    }

    /// <summary>A verification that could not run is not recorded as a failure and mails nothing.</summary>
    /// <remarks>
    /// An agent that is saturated has said nothing about the artifact. Marking the backup failed
    /// would mail "your backup is corrupt" over a busy host; the backup keeps the state it had.
    /// </remarks>
    [Fact]
    public async Task A_verification_that_could_not_run_leaves_the_backup_unverified_and_mails_nothing()
    {
        var agent = new ScriptedVerificationAgentBackupClient();
        var mailer = new RecordingMailer(TimeSpan.Zero);
        await using var factory = await BackupVerificationWorld.BootAsync(
            _pg, agent, mailer, alertRecipient: true);
        agent.AnswerFailure("AgentSaturated", ErrorType.Unavailable);

        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        var backupId = await BackupVerificationWorld.CreateManualBackupAsync(
            owner, await BackupVerificationWorld.AccountIdAsync(factory, "acme"));
        await BackupVerificationWorld.WaitForAgentVerificationsAsync(agent, 1);

        var row = (await BackupVerificationWorld.BackupsAsync(factory)).Single();
        Assert.Equal(backupId, row.Id);
        Assert.Equal(BackupVerificationState.NotVerified, row.VerificationState);
        Assert.Equal(BackupStatus.Completed, row.Status);

        Assert.Empty(await AlertsAsync(factory));
        Assert.Empty(await BackupVerificationWorld.DrainedMailAsync(factory, mailer));
    }

    /// <summary>With no alert address configured the verification alert is journalled and the mail skipped.</summary>
    /// <remarks>
    /// Nobody to tell is the ordinary state of a fresh installation, so the skip is journalled as
    /// SUCCEEDED beside the alert itself, and no mail is requested. This leaves out the fixed
    /// recipient on purpose: the host's real directory answers nobody.
    /// </remarks>
    [Fact]
    public async Task With_no_alert_address_the_verification_alert_is_journalled_and_the_mail_is_skipped()
    {
        var agent = new ScriptedVerificationAgentBackupClient();
        var mailer = new RecordingMailer(TimeSpan.Zero);
        await using var factory = await BackupVerificationWorld.BootAsync(
            _pg, agent, mailer, alertRecipient: false);
        agent.AnswerFailure("AgentValidationFailed", ErrorType.Validation);

        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        var backupId = await BackupVerificationWorld.CreateManualBackupAsync(
            owner, await BackupVerificationWorld.AccountIdAsync(factory, "acme"));
        await BackupVerificationWorld.WaitForVerificationAsync(factory, backupId);

        var alert = Assert.Single(await AlertsAsync(factory));
        Assert.Equal($"{AlertSubjectPrefix}{backupId}", alert.Subject);

        var skipped = Assert.Single(await BackupVerificationWorld.WaitForAuditAsync(
            factory, MailAuditActions.MailSkippedNoSmtp, 1));
        Assert.Equal(alert.Subject, skipped.Subject);
        Assert.True(skipped.Succeeded);

        Assert.Empty(await BackupVerificationWorld.DrainedMailAsync(factory, mailer));
    }

    /// <summary>Reads the verification alerts the journal holds.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>Every <c>AlertRaised</c> entry whose subject names a failed verification.</returns>
    private static async Task<IReadOnlyList<AuditEvent>> AlertsAsync(
        WebApplicationFactory<Program> factory)
    {
        var raised = await BackupVerificationWorld.AuditEntriesAsync(factory, MonitoringAuditActions.AlertRaised);

        return raised.Where(entry => { return entry.Subject.StartsWith(AlertSubjectPrefix, StringComparison.Ordinal); })
            .ToList();
    }

    /// <summary>Asserts a mail names the account and the backup's time and carries no digest or path.</summary>
    /// <param name="mail">The mail the administrator was sent.</param>
    /// <param name="backup">The backup row the alert is about, as the database holds it.</param>
    /// <remarks>
    /// The mail does not name the backup's identifier — the journal subject does — and it says what
    /// kind of problem was found and nothing about where: no digest, no size, no path.
    /// </remarks>
    private static void AssertMailNamesAccountAndRun(SentMail mail, Backup backup)
    {
        Assert.Contains("acme", mail.Subject, StringComparison.Ordinal);
        Assert.Contains("acme", mail.Body, StringComparison.Ordinal);
        Assert.Contains(
            backup.FinishedAt!.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            mail.Body,
            StringComparison.Ordinal);
        Assert.DoesNotContain(new string('0', 64), mail.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(backup.Id.ToString(), mail.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/", mail.Body, StringComparison.Ordinal);
    }

    /// <summary>Runs the scheduled sweep ten minutes after the named hour of a schedule enabled just now.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="clock">The clock the host reads, which this moves.</param>
    /// <returns>The task that seeds the schedule, moves the clock and runs the sweep once.</returns>
    private static async Task RunSweepTenMinutesLateAsync(WebApplicationFactory<Program> factory, AdvancingClock clock)
    {
        var start = clock.UtcNow;
        var nextHour = new DateTimeOffset(start.Year, start.Month, start.Day, start.Hour, 0, 0, TimeSpan.Zero)
            .AddHours(1);

        await BackupVerificationWorld.SeedDailyScheduleAsync(factory, "acme", null, nextHour.Hour, start);
        clock.Advance(nextHour - start + TimeSpan.FromMinutes(10));

        using var scope = factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<BackupRunHandler>();

        Assert.Equal(1, await handler.HandleAsync(new BackupRunRequested(), CancellationToken.None));
    }
}
