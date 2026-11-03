using System.Net;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// <c>POST /api/v1/backups/{id}/verify</c> over real HTTP and real PostgreSQL: who may ask for a
/// verification, that another tenant's backup answers 404 without the agent being asked, that a
/// look-through-the-customer's-eyes session is refused, and what each answer leaves on the row and
/// in the journal.
/// </summary>
/// <remarks>
/// <para>
/// <b>The agent's call log is the proof of "nothing reached the agent".</b> Every refusal below
/// asserts that <see cref="ScriptedVerificationAgentBackupClient.Verifications"/> stayed empty,
/// because a handler that asked the agent first and refused afterwards would pass a status-code
/// assertion and have already made the only root process read another tenant's artifact.
/// </para>
/// <para>
/// <b>Where the identifiers come from.</b> The route carries only the backup's id; the account and
/// destination the agent is asked about are read from the row, which the tests prove by the account
/// name the scripted agent records.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class BackupVerificationEndpointTests : IAsyncLifetime
{
    /// <summary>The database this test class owns on the shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public BackupVerificationEndpointTests(PostgresFixture postgres)
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

    /// <summary>An anonymous caller is refused and the agent is not asked.</summary>
    [Fact]
    public async Task An_anonymous_caller_is_refused_and_the_agent_is_not_asked()
    {
        var agent = new ScriptedVerificationAgentBackupClient();
        await using var factory = await BackupVerificationWorld.BootAsync(
            _pg, agent, new RecordingMailer(TimeSpan.Zero), alertRecipient: true);
        var backupId = await BackupVerificationWorld.SeedCompletedBackupAsync(factory, "acme");

        using var anonymous = factory.CreateClient();
        var response = await anonymous.PostAsync($"/api/v1/backups/{backupId}/verify", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(agent.Verifications);
    }

    /// <summary>The owner verifies their own backup, which is then recorded verified and journalled.</summary>
    [Fact]
    public async Task The_owner_verifies_their_own_backup_and_it_is_recorded_and_journalled()
    {
        var agent = new ScriptedVerificationAgentBackupClient();
        await using var factory = await BackupVerificationWorld.BootAsync(
            _pg, agent, new RecordingMailer(TimeSpan.Zero), alertRecipient: true);
        agent.AnswerSound();
        var backupId = await BackupVerificationWorld.SeedCompletedBackupAsync(factory, "acme");

        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        var response = await owner.PostAsync($"/api/v1/backups/{backupId}/verify", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(backupId, body.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("verified", body.RootElement.GetProperty("verificationState").GetString(), ignoreCase: true);
        Assert.Equal(JsonValueKind.String, body.RootElement.GetProperty("verifiedAt").ValueKind);

        // The agent was asked about the account and backup the ROW names: the route carried only the id.
        var asked = Assert.Single(agent.Verifications);
        Assert.Equal(("acme", backupId.ToString()), asked);

        var row = Assert.Single(await BackupVerificationWorld.BackupsAsync(factory));
        Assert.Equal(BackupVerificationState.Verified, row.VerificationState);
        Assert.Null(row.VerificationFailureCode);

        var entry = Assert.Single(await BackupVerificationWorld.AuditEntriesAsync(
            factory, BackupAuditActions.BackupVerified));
        Assert.True(entry.Succeeded);
        Assert.Equal("owner-a", entry.ActorUsername);
    }

    /// <summary>A finding about the artifact is recorded as failed, journalled as a failure, and not mailed.</summary>
    /// <remarks>
    /// "Verify now" is the owner's own question, answered on their own screen. The administrator's
    /// mail is raised once when a run is verified, so a login clicking the button cannot use it as a
    /// mail cannon against the administrator; the run-time alert is proved in
    /// <see cref="BackupVerificationAlertTests"/>.
    /// </remarks>
    [Fact]
    public async Task A_finding_about_the_artifact_is_recorded_failed_journalled_and_not_mailed()
    {
        var agent = new ScriptedVerificationAgentBackupClient();
        var mailer = new RecordingMailer(TimeSpan.Zero);
        await using var factory = await BackupVerificationWorld.BootAsync(
            _pg, agent, mailer, alertRecipient: true);
        agent.AnswerFailure("AgentValidationFailed", ErrorType.Validation);
        var backupId = await BackupVerificationWorld.SeedCompletedBackupAsync(factory, "acme");

        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        await owner.PostAsync($"/api/v1/backups/{backupId}/verify", null);

        var row = Assert.Single(await BackupVerificationWorld.BackupsAsync(factory));
        Assert.Equal(BackupVerificationState.Failed, row.VerificationState);
        Assert.Equal(BackupStatus.Completed, row.Status);
        Assert.False(string.IsNullOrWhiteSpace(row.VerificationFailureCode));

        var entry = Assert.Single(await BackupVerificationWorld.AuditEntriesAsync(
            factory, BackupAuditActions.BackupVerificationFailed));
        Assert.False(entry.Succeeded);
        Assert.Equal("owner-a", entry.ActorUsername);

        Assert.Empty(await BackupVerificationWorld.AuditEntriesAsync(factory, MonitoringAuditActions.AlertRaised));
        Assert.Empty(await BackupVerificationWorld.DrainedMailAsync(factory, mailer));
    }

    /// <summary>A later verification replaces an earlier finding: the last answer wins.</summary>
    [Fact]
    public async Task A_later_sound_verification_replaces_an_earlier_failure()
    {
        var agent = new ScriptedVerificationAgentBackupClient();
        await using var factory = await BackupVerificationWorld.BootAsync(
            _pg, agent, new RecordingMailer(TimeSpan.Zero), alertRecipient: true);
        var backupId = await BackupVerificationWorld.SeedCompletedBackupAsync(factory, "acme");
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");

        agent.AnswerFailure("AgentValidationFailed", ErrorType.Validation);
        await owner.PostAsync($"/api/v1/backups/{backupId}/verify", null);
        var failed = Assert.Single(await BackupVerificationWorld.BackupsAsync(factory));
        Assert.Equal(BackupVerificationState.Failed, failed.VerificationState);

        agent.AnswerSound();
        var response = await owner.PostAsync($"/api/v1/backups/{backupId}/verify", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = Assert.Single(await BackupVerificationWorld.BackupsAsync(factory));
        Assert.Equal(BackupVerificationState.Verified, row.VerificationState);
        Assert.Null(row.VerificationFailureCode);
        Assert.Equal(2, agent.VerificationCount);
    }

    /// <summary>A verification the agent could not carry out leaves the state alone.</summary>
    [Fact]
    public async Task A_verification_that_could_not_run_does_not_mark_the_backup_failed()
    {
        var agent = new ScriptedVerificationAgentBackupClient();
        await using var factory = await BackupVerificationWorld.BootAsync(
            _pg, agent, new RecordingMailer(TimeSpan.Zero), alertRecipient: true);
        agent.AnswerFailure("AgentSaturated", ErrorType.Unavailable);
        var backupId = await BackupVerificationWorld.SeedCompletedBackupAsync(factory, "acme");

        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        var response = await owner.PostAsync($"/api/v1/backups/{backupId}/verify", null);

        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal(1, agent.VerificationCount);

        var row = Assert.Single(await BackupVerificationWorld.BackupsAsync(factory));
        Assert.Equal(BackupVerificationState.NotVerified, row.VerificationState);
        Assert.Null(row.VerifiedAt);
        Assert.Null(row.VerificationFailureCode);
    }

    /// <summary>Another account's backup answers 404, never 403, and the agent is not asked.</summary>
    [Fact]
    public async Task Another_accounts_backup_answers_not_found_and_the_agent_is_not_asked()
    {
        var agent = new ScriptedVerificationAgentBackupClient();
        await using var factory = await BackupVerificationWorld.BootAsync(
            _pg, agent, new RecordingMailer(TimeSpan.Zero), alertRecipient: true);
        var foreignBackup = await BackupVerificationWorld.SeedCompletedBackupAsync(factory, "beta");

        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        var foreign = await owner.PostAsync($"/api/v1/backups/{foreignBackup}/verify", null);
        var unknown = await owner.PostAsync($"/api/v1/backups/{Guid.NewGuid()}/verify", null);

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        // The two answers are the same, so the status cannot be used to learn which ids exist.
        Assert.Equal(await ProblemDocument.CodeAsync(unknown), await ProblemDocument.CodeAsync(foreign));

        Assert.Empty(agent.Verifications);
        var row = Assert.Single(await BackupVerificationWorld.BackupsAsync(factory));
        Assert.Equal(BackupVerificationState.NotVerified, row.VerificationState);
        Assert.Empty(await BackupVerificationWorld.AuditEntriesAsync(
            factory, BackupAuditActions.BackupVerified, BackupAuditActions.BackupVerificationFailed));
    }

    /// <summary>An administrator may verify any account's backup, and the journal names them.</summary>
    /// <remarks>
    /// The inverse control of the refusal above: a route mutated to refuse everyone but the owner
    /// passes the 404 test and fails here.
    /// </remarks>
    [Fact]
    public async Task An_administrator_verifies_any_accounts_backup()
    {
        var agent = new ScriptedVerificationAgentBackupClient();
        await using var factory = await BackupVerificationWorld.BootAsync(
            _pg, agent, new RecordingMailer(TimeSpan.Zero), alertRecipient: true);
        agent.AnswerSound();
        var backupId = await BackupVerificationWorld.SeedCompletedBackupAsync(factory, "beta");

        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        var response = await admin.PostAsync($"/api/v1/backups/{backupId}/verify", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(("beta", backupId.ToString()), Assert.Single(agent.Verifications));

        var row = Assert.Single(await BackupVerificationWorld.BackupsAsync(factory));
        Assert.Equal(BackupVerificationState.Verified, row.VerificationState);

        var entry = Assert.Single(await BackupVerificationWorld.AuditEntriesAsync(
            factory, BackupAuditActions.BackupVerified));
        Assert.Equal("admin", entry.ActorUsername);
    }

    /// <summary>An operator viewing as the owner is refused with the view-as code and the agent is not asked.</summary>
    [Fact]
    public async Task A_view_as_session_is_refused_with_the_view_as_code_and_the_agent_is_not_asked()
    {
        var agent = new ScriptedVerificationAgentBackupClient();
        await using var factory = await BackupVerificationWorld.BootAsync(
            _pg, agent, new RecordingMailer(TimeSpan.Zero), alertRecipient: true);
        var backupId = await BackupVerificationWorld.SeedCompletedBackupAsync(factory, "acme");

        using var viewing = await SubUserTeamWorld.StartViewingAsync(factory, "acme");
        var response = await viewing.PostAsync($"/api/v1/backups/{backupId}/verify", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("HostViewAsNotAllowed", await ProblemDocument.CodeAsync(response));
        Assert.Empty(agent.Verifications);

        var row = Assert.Single(await BackupVerificationWorld.BackupsAsync(factory));
        Assert.Equal(BackupVerificationState.NotVerified, row.VerificationState);
    }
}
