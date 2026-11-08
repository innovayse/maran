using Maran.Agent.Client.Services.BackupService;
using Maran.Agent.Simulated.Errors;
using Maran.Agent.Simulated.Limits;
using Maran.Agent.Simulated.Services.BackupService;
using Maran.Agent.Simulated.State;
using Maran.Agent.Simulated.Tests.TestSupport;
using Maran.SharedKernel.Security;

namespace Maran.Agent.Simulated.Tests.Services.BackupService;

/// <summary>The simulated agent's backup operations answer like the real agent's and write no archive.</summary>
public sealed class SimulatedAgentBackupClientTests
{
    /// <summary>The event kinds of a backup that runs to completion.</summary>
    private static readonly BackupCreateEventKind[] CreatedKinds =
    [
        BackupCreateEventKind.Progress,
        BackupCreateEventKind.Progress,
        BackupCreateEventKind.Created,
    ];

    /// <summary>The event kinds of a backup cancelled part-way.</summary>
    private static readonly BackupCreateEventKind[] CancelledKinds =
    [
        BackupCreateEventKind.Progress,
        BackupCreateEventKind.Cancelled,
    ];

    /// <summary>The stages a backup reports before it is created.</summary>
    private static readonly string[] CreateStages = ["archiving_files", "dumping_databases"];

    /// <summary>The event kinds of a restore that runs to completion.</summary>
    private static readonly BackupRestoreEventKind[] RestoredKinds =
    [
        BackupRestoreEventKind.Progress,
        BackupRestoreEventKind.Progress,
        BackupRestoreEventKind.Restored,
    ];

    /// <summary>The stages a restore reports before it finishes.</summary>
    private static readonly string[] RestoreStages = ["restoring_files", "restoring_databases"];

    /// <summary>The only destination the agent that ships today reaches.</summary>
    private static readonly AgentBackupDestination Local = new AgentLocalBackupDestination();

    /// <summary>A destination naming a bucket, which the agent refuses with not implemented.</summary>
    private static readonly AgentBackupDestination Bucket = new AgentS3BackupDestination(
        "backups/",
        "bucket",
        "eu-central-1",
        "https://s3.example.invalid",
        new SensitiveString("AKIDEXAMPLE"),
        new SensitiveString("secret"),
        false);

    /// <summary>An artifact the agent could not read, the way a seed gives a screen a corrupt backup.</summary>
    private static readonly AgentBackupSummary Broken = new("broken", null, new AgentBackupCorrupt());

    /// <summary>
    /// A create stream reports progress and ends in exactly one Created terminal event carrying the artifact's facts.
    /// </summary>
    [Fact]
    public async Task A_create_stream_reports_progress_and_ends_in_exactly_one_created_event()
    {
        var (client, ledger) = NewClient();
        ledger.Databases.Create("alice", "shop", "u", 4_000_000UL);

        var events = await Create(client, "alice", "b1");

        Assert.Equal(
            CreatedKinds,
            events.Select(item => { return item.Kind; }));
        Assert.Equal(CreateStages, events.Take(2).Select(item =>
        {
            return item.Stage;
        }));
        var created = events[^1];
        Assert.True(created.SizeBytes > 0);
        Assert.Equal(64, created.Sha256.Length);
        Assert.Equal(1UL, created.DatabaseCount);
        Assert.Null(created.ErrorCode);
    }

    /// <summary>The terminal event matches what the artifact is listed with.</summary>
    [Fact]
    public async Task The_terminal_event_matches_what_the_artifact_is_listed_with()
    {
        var (client, _) = NewClient();
        var events = await Create(client, "alice", "b1");

        var listed = await client.ListAsync("alice", Local, CancellationToken.None);

        var summary = Assert.Single(listed.Value);
        Assert.Equal("b1", summary.BackupId);
        Assert.Null(summary.Unreadable);
        Assert.Equal(events[^1].SizeBytes, summary.Readable!.ArtifactBytes);
        Assert.Equal(events[^1].Sha256, summary.Readable.ArtifactSha256);
        Assert.Equal("alice", summary.Readable.Manifest.Account);
        Assert.Equal("b1", summary.Readable.Manifest.BackupId);
        Assert.Equal(new FixedClock().UtcNow.ToUnixTimeSeconds(), summary.Readable.Manifest.CreatedAtUnix);
    }

    /// <summary>The manifest names the account's databases with their sizes, and no other account's.</summary>
    [Fact]
    public async Task The_manifest_names_the_accounts_databases_with_their_sizes_and_no_other_accounts()
    {
        var (client, ledger) = NewClient();
        ledger.Databases.Create("alice", "shop", "u", 4_000_000UL);
        ledger.Databases.Create("bob", "other", "u", 9UL);

        await Create(client, "alice", "b1");

        var listed = await client.ListAsync("alice", Local, CancellationToken.None);
        var manifest = Assert.Single(listed.Value).Readable!.Manifest;
        var database = Assert.Single(manifest.Databases);
        Assert.Equal("alice_shop", database.Name);
        Assert.Equal(4_000_000UL, database.Bytes);
        Assert.Equal(64, database.Sha256.Length);
    }

    /// <summary>
    /// Creating a backup id that already completed is answered with a single Created event and the same artifact.
    /// </summary>
    [Fact]
    public async Task Creating_a_backup_id_that_already_completed_is_answered_immediately_with_the_same_artifact()
    {
        var (client, ledger) = NewClient();
        var first = await Create(client, "alice", "b1");
        ledger.Databases.Create("alice", "added-later", "u", 1UL);

        var again = await Create(client, "alice", "b1");

        var created = Assert.Single(again);
        Assert.Equal(BackupCreateEventKind.Created, created.Kind);
        Assert.Equal(first[^1].Sha256, created.Sha256);
        Assert.Equal(first[^1].SizeBytes, created.SizeBytes);
        Assert.Single((await client.ListAsync("alice", Local, CancellationToken.None)).Value);
    }

    /// <summary>
    /// The same actions on two servers describe the same artifact, and two accounts describe different ones.
    /// </summary>
    [Fact]
    public async Task The_same_actions_on_two_servers_describe_the_same_artifact_and_two_accounts_different_ones()
    {
        var (firstClient, _) = NewClient();
        var (secondClient, _) = NewClient();

        var first = await Create(firstClient, "alice", "b1");
        var second = await Create(secondClient, "alice", "b1");
        var other = await Create(secondClient, "bob", "b1");

        Assert.Equal(first[^1], second[^1]);
        Assert.NotEqual(first[^1].Sha256, other[^1].Sha256);
    }

    /// <summary>
    /// A create stream for a destination naming a bucket ends in one Failed event with not implemented and stores
    /// nothing.
    /// </summary>
    [Fact]
    public async Task A_create_stream_for_a_bucket_ends_in_one_failed_event_and_stores_nothing()
    {
        var (client, _) = NewClient();

        var events = await AsyncStreams.CollectAsync(client.CreateAsync("alice", "b1", Bucket, CancellationToken.None));

        var failed = Assert.Single(events);
        Assert.Equal(BackupCreateEventKind.Failed, failed.Kind);
        Assert.Equal(SimulatedAgentErrors.NotImplementedCode, failed.ErrorCode);
        Assert.Empty((await client.ListAsync("alice", Local, CancellationToken.None)).Value);
    }

    /// <summary>
    /// A create stream whose caller already cancelled ends in one Cancelled event and stores nothing.
    /// </summary>
    [Fact]
    public async Task A_create_stream_whose_caller_cancelled_ends_in_one_cancelled_event_and_stores_nothing()
    {
        var (client, _) = NewClient();
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        var events = await AsyncStreams.CollectAsync(client.CreateAsync("alice", "b1", Local, source.Token));

        Assert.Equal(BackupCreateEventKind.Cancelled, Assert.Single(events).Kind);
        Assert.Empty((await client.ListAsync("alice", Local, CancellationToken.None)).Value);
    }

    /// <summary>
    /// A create stream cancelled between steps ends in one Cancelled event after the progress already sent.
    /// </summary>
    [Fact]
    public async Task A_create_stream_cancelled_between_steps_ends_in_one_cancelled_event()
    {
        var (client, _) = NewClient();
        using var source = new CancellationTokenSource();
        var events = new List<BackupCreateEvent>();

        await foreach (var item in client.CreateAsync("alice", "b1", Local, source.Token))
        {
            events.Add(item);
            if (events.Count == 1)
            {
                await source.CancelAsync();
            }
        }

        Assert.Equal(
            CancelledKinds,
            events.Select(item => { return item.Kind; }));
        Assert.Empty((await client.ListAsync("alice", Local, CancellationToken.None)).Value);
    }

    /// <summary>
    /// An account holds at most the ceiling of artifacts; the next create ends in a Failed event with limit reached.
    /// </summary>
    [Fact]
    public async Task Creating_beyond_the_account_ceiling_ends_in_a_failed_event_with_limit_reached()
    {
        var (client, _) = NewClient();
        for (var index = 0; index < SimulatedAgentLimits.Default.MaxBackupsPerAccount; index++)
        {
            var events = await Create(client, "alice", $"b{index}");
            Assert.Equal(BackupCreateEventKind.Created, events[^1].Kind);
        }

        var over = await Create(client, "alice", "one-too-many");
        var other = await Create(client, "bob", "b0");

        Assert.Equal(BackupCreateEventKind.Failed, over[^1].Kind);
        Assert.Equal(SimulatedAgentErrors.LimitReachedCode, over[^1].ErrorCode);
        Assert.Equal(BackupCreateEventKind.Created, other[^1].Kind);
    }

    /// <summary>Listing a destination naming a bucket answers not implemented, never an empty list.</summary>
    [Fact]
    public async Task Listing_a_bucket_answers_not_implemented_and_never_an_empty_list()
    {
        var (client, _) = NewClient();

        var listed = await client.ListAsync("alice", Bucket, CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.NotImplementedCode, listed.Error!.Code);
    }

    /// <summary>A backup seeded as unreadable is listed with its reason and carries no manifest.</summary>
    [Fact]
    public async Task A_backup_seeded_as_unreadable_is_listed_with_its_reason_and_no_manifest()
    {
        var (client, ledger) = NewClient();
        ledger.Backups.GetOrAdd("alice", "broken", () => { return Broken; });

        var listed = await client.ListAsync("alice", Local, CancellationToken.None);

        Assert.Equal(Broken, Assert.Single(listed.Value));
    }

    /// <summary>Verifying a readable backup reports its members, its databases and its size.</summary>
    [Fact]
    public async Task Verifying_a_readable_backup_reports_its_members_databases_and_size()
    {
        var (client, ledger) = NewClient();
        ledger.Databases.Create("alice", "shop", "u", 4_000_000UL);
        var events = await Create(client, "alice", "b1");

        var verified = await client.VerifyBackupAsync("alice", "b1", Local, CancellationToken.None);

        Assert.Equal(new AgentBackupVerification(3, 1, events[^1].SizeBytes), verified.Value);
    }

    /// <summary>
    /// Verifying a missing backup answers not found, an unreadable one a failure, and a bucket not implemented.
    /// </summary>
    [Fact]
    public async Task Verifying_a_missing_unreadable_or_remote_backup_is_a_failure_with_its_own_code()
    {
        var (client, ledger) = NewClient();
        ledger.Backups.GetOrAdd("alice", "broken", () => { return Broken; });

        var missing = await client.VerifyBackupAsync("alice", "ghost", Local, CancellationToken.None);
        var unreadable = await client.VerifyBackupAsync("alice", "broken", Local, CancellationToken.None);
        var remote = await client.VerifyBackupAsync("alice", "broken", Bucket, CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.NotFoundCode, missing.Error!.Code);
        Assert.Equal(SimulatedAgentErrors.ValidationFailedCode, unreadable.Error!.Code);
        Assert.Equal(SimulatedAgentErrors.NotImplementedCode, remote.Error!.Code);
    }

    /// <summary>
    /// Deleting removes the artifact, a second delete answers not found, and a bucket is not implemented.
    /// </summary>
    [Fact]
    public async Task Deleting_removes_the_artifact_and_a_second_delete_answers_not_found()
    {
        var (client, _) = NewClient();
        await Create(client, "alice", "b1");

        var remote = await client.DeleteAsync("alice", "b1", Bucket, CancellationToken.None);
        var deleted = await client.DeleteAsync("alice", "b1", Local, CancellationToken.None);
        var again = await client.DeleteAsync("alice", "b1", Local, CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.NotImplementedCode, remote.Error!.Code);
        Assert.True(deleted.IsSuccess);
        Assert.Equal(SimulatedAgentErrors.NotFoundCode, again.Error!.Code);
    }

    /// <summary>Another account cannot verify, delete or restore a backup it does not own.</summary>
    [Fact]
    public async Task Another_account_cannot_verify_delete_or_restore_a_backup_it_does_not_own()
    {
        var (client, _) = NewClient();
        var events = await Create(client, "alice", "b1");

        var verify = await client.VerifyBackupAsync("bob", "b1", Local, CancellationToken.None);
        var delete = await client.DeleteAsync("bob", "b1", Local, CancellationToken.None);
        var restore = await AsyncStreams.CollectAsync(
            client.RestoreAsync(
                "bob", "b1", Local, events[^1].Sha256, [], new AgentWholeRestoreScope(), CancellationToken.None));

        Assert.Equal(SimulatedAgentErrors.NotFoundCode, verify.Error!.Code);
        Assert.Equal(SimulatedAgentErrors.NotFoundCode, delete.Error!.Code);
        Assert.Equal(SimulatedAgentErrors.NotFoundCode, Assert.Single(restore).Failure!.Code);
        Assert.Single((await client.ListAsync("alice", Local, CancellationToken.None)).Value);
    }

    /// <summary>
    /// A probe answers not implemented for every destination, so it can never be read as permission to save one.
    /// </summary>
    [Fact]
    public async Task A_probe_answers_not_implemented_for_every_destination()
    {
        var (client, _) = NewClient();

        var local = await client.ProbeAsync(Local, CancellationToken.None);
        var bucket = await client.ProbeAsync(Bucket, CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.NotImplementedCode, local.Error!.Code);
        Assert.Equal(SimulatedAgentErrors.NotImplementedCode, bucket.Error!.Code);
    }

    /// <summary>
    /// A restore with the recorded digest and the account's databases reports progress and one Restored event.
    /// </summary>
    [Fact]
    public async Task A_restore_with_the_recorded_digest_reports_progress_and_one_restored_event()
    {
        var (client, ledger) = NewClient();
        ledger.Databases.Create("alice", "shop", "u", 4_000_000UL);
        var created = (await Create(client, "alice", "b1"))[^1];

        var events = await AsyncStreams.CollectAsync(
            client.RestoreAsync(
                "alice", "b1", Local, created.Sha256, ["alice_shop"], new AgentWholeRestoreScope(), CancellationToken.None));

        Assert.Equal(
            RestoredKinds,
            events.Select(item => { return item.Kind; }));
        Assert.Equal(RestoreStages, events.Take(2).Select(item =>
        {
            return item.Stage;
        }));
        Assert.Equal(new AgentRestoreOutcome(true, 1, 1), events[^1].Outcome);
        Assert.Null(events[^1].Failure);
    }

    /// <summary>A restore of an account with no database restores the files and needs no allowed databases.</summary>
    [Fact]
    public async Task A_restore_of_an_account_with_no_database_needs_no_allowed_databases()
    {
        var (client, _) = NewClient();
        var created = (await Create(client, "alice", "b1"))[^1];

        var events = await AsyncStreams.CollectAsync(
            client.RestoreAsync(
                "alice", "b1", Local, created.Sha256, [], new AgentWholeRestoreScope(), CancellationToken.None));

        Assert.Equal(new AgentRestoreOutcome(true, 0, 0), events[^1].Outcome);
    }

    /// <summary>
    /// A restore the agent refuses ends in one Failed event before any progress, carrying the refusal.
    /// </summary>
    [Fact]
    public async Task A_refused_restore_ends_in_one_failed_event_before_any_progress()
    {
        var (client, ledger) = NewClient();
        ledger.Databases.Create("alice", "shop", "u", 4_000_000UL);
        var created = (await Create(client, "alice", "b1"))[^1];

        var emptyDigest = await Restore(client, "b1", Local, string.Empty, ["alice_shop"]);
        var wrongDigest = await Restore(client, "b1", Local, new string('0', 64), ["alice_shop"]);
        var unknownBackup = await Restore(client, "ghost", Local, created.Sha256, ["alice_shop"]);
        var databaseNotAllowed = await Restore(client, "b1", Local, created.Sha256, []);
        var bucket = await Restore(client, "b1", Bucket, created.Sha256, ["alice_shop"]);

        Assert.Equal(SimulatedAgentErrors.InvalidInputCode, emptyDigest.Failure!.Code);
        Assert.Equal(SimulatedAgentErrors.ValidationFailedCode, wrongDigest.Failure!.Code);
        Assert.Equal(SimulatedAgentErrors.NotFoundCode, unknownBackup.Failure!.Code);
        Assert.Equal(SimulatedAgentErrors.ValidationFailedCode, databaseNotAllowed.Failure!.Code);
        Assert.Equal(SimulatedAgentErrors.NotImplementedCode, bucket.Failure!.Code);
        Assert.All(
            new[] { emptyDigest, wrongDigest, unknownBackup, databaseNotAllowed, bucket },
            terminal => { Assert.Equal(BackupRestoreEventKind.Failed, terminal.Kind); });
    }

    /// <summary>A restore from an artifact seeded as unreadable is refused whole.</summary>
    [Fact]
    public async Task A_restore_from_an_unreadable_artifact_is_refused_whole()
    {
        var (client, ledger) = NewClient();
        ledger.Backups.GetOrAdd("alice", "broken", () => { return Broken; });

        var terminal = await Restore(client, "broken", Local, new string('a', 64), []);

        Assert.Equal(BackupRestoreEventKind.Failed, terminal.Kind);
        Assert.Equal(SimulatedAgentErrors.ValidationFailedCode, terminal.Failure!.Code);
    }

    /// <summary>A restore stream whose caller cancelled ends in one Cancelled event.</summary>
    [Fact]
    public async Task A_restore_stream_whose_caller_cancelled_ends_in_one_cancelled_event()
    {
        var (client, _) = NewClient();
        var created = (await Create(client, "alice", "b1"))[^1];
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        var events = await AsyncStreams.CollectAsync(
            client.RestoreAsync(
                "alice", "b1", Local, created.Sha256, [], new AgentWholeRestoreScope(), source.Token));

        Assert.Equal(BackupRestoreEventKind.Cancelled, Assert.Single(events).Kind);
    }

    /// <summary>Runs a create stream for the local destination and reads it to its end.</summary>
    /// <param name="client">The client under test.</param>
    /// <param name="account">System username of the account being backed up.</param>
    /// <param name="backupId">The backup identifier.</param>
    /// <returns>Every event the stream produced.</returns>
    private static Task<List<BackupCreateEvent>> Create(
        SimulatedAgentBackupClient client,
        string account,
        string backupId)
    {
        return AsyncStreams.CollectAsync(client.CreateAsync(account, backupId, Local, CancellationToken.None));
    }

    /// <summary>Runs a restore for alice and returns its only event, which the caller asserts is terminal.</summary>
    /// <param name="client">The client under test.</param>
    /// <param name="backupId">The backup to restore.</param>
    /// <param name="destination">Where the artifact resides.</param>
    /// <param name="expectedSha256">The digest the caller recorded.</param>
    /// <param name="allowedDatabases">The databases the caller still knows the account owns.</param>
    /// <returns>The single event the stream produced.</returns>
    private static async Task<BackupRestoreEvent> Restore(
        SimulatedAgentBackupClient client,
        string backupId,
        AgentBackupDestination destination,
        string expectedSha256,
        IReadOnlyList<string> allowedDatabases)
    {
        var events = await AsyncStreams.CollectAsync(
            client.RestoreAsync(
                "alice",
                backupId,
                destination,
                expectedSha256,
                allowedDatabases,
                new AgentWholeRestoreScope(),
                CancellationToken.None));

        return Assert.Single(events);
    }

    /// <summary>Builds a client over a fresh ledger and a fixed clock, and returns both.</summary>
    /// <returns>The client and the ledger it reads, so a test can seed and inspect the state.</returns>
    private static (SimulatedAgentBackupClient Client, SimulatedAgentLedger Ledger) NewClient()
    {
        var ledger = new SimulatedAgentLedger(SimulatedAgentLimits.Default);

        return (new SimulatedAgentBackupClient(ledger, new FixedClock()), ledger);
    }
}
