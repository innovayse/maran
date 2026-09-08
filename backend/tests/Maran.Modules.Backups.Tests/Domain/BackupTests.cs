using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;

namespace Maran.Modules.Backups.Tests.Domain;

/// <summary>The one transition a backup row makes, and the states it refuses to make it from.</summary>
public sealed class BackupTests
{
    /// <summary>When the run began, so nothing here reads the ambient clock.</summary>
    private static readonly DateTimeOffset Started = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>When the run ended.</summary>
    private static readonly DateTimeOffset Finished = Started.AddMinutes(3);

    /// <summary>A digest of the shape the agent reports.</summary>
    private static readonly string Digest = new('c', 64);

    /// <summary>A new backup is running, unfinished and nameless of any destination.</summary>
    [Fact]
    public void A_new_backup_is_running_and_unfinished()
    {
        var backup = new Backup(Guid.NewGuid(), Guid.NewGuid(), destinationId: null, BackupKind.Manual, Started);

        Assert.Equal(BackupStatus.Running, backup.Status);
        Assert.Null(backup.FinishedAt);
        Assert.Null(backup.DestinationId);
        Assert.Equal(string.Empty, backup.Sha256);
        Assert.Equal(string.Empty, backup.FailureCode);
    }

    /// <summary>Completing a running backup records what the agent produced.</summary>
    [Fact]
    public void Completing_a_running_backup_records_what_the_agent_produced()
    {
        var backup = Running();

        backup.Completed(2048, Digest, 2, Finished);

        Assert.Equal(BackupStatus.Completed, backup.Status);
        Assert.Equal(2048, backup.SizeBytes);
        Assert.Equal(Digest, backup.Sha256);
        Assert.Equal(2, backup.DatabaseCount);
        Assert.Equal(Finished, backup.FinishedAt);
    }

    /// <summary>Completing without a digest is refused, so no row offers an unverifiable restore.</summary>
    /// <remarks>
    /// Its inverse control is <see cref="Completing_a_running_backup_records_what_the_agent_produced"/>
    /// above: a guard mutated to refuse every completion passes this test alone.
    /// </remarks>
    [Fact]
    public void Completing_without_a_digest_is_refused()
    {
        var backup = Running();

        backup.Completed(2048, string.Empty, 2, Finished);

        Assert.Equal(BackupStatus.Running, backup.Status);
        Assert.Equal(0, backup.SizeBytes);
        Assert.Null(backup.FinishedAt);
    }

    /// <summary>Failing a running backup records the code and no artifact figures.</summary>
    [Fact]
    public void Failing_a_running_backup_records_the_code_and_no_artifact_figures()
    {
        var backup = Running();

        backup.Failed("BackupTruncated", Finished);

        Assert.Equal(BackupStatus.Failed, backup.Status);
        Assert.Equal("BackupTruncated", backup.FailureCode);
        Assert.Equal(0, backup.SizeBytes);
        Assert.Equal(string.Empty, backup.Sha256);
    }

    /// <summary>A finished backup does not move again.</summary>
    [Fact]
    public void A_finished_backup_does_not_move_again()
    {
        var completed = Running();
        completed.Completed(2048, Digest, 2, Finished);
        completed.Failed("BackupTruncated", Finished.AddMinutes(1));

        Assert.Equal(BackupStatus.Completed, completed.Status);
        Assert.Equal(string.Empty, completed.FailureCode);

        var failed = Running();
        failed.Failed("BackupTruncated", Finished);
        failed.Completed(2048, Digest, 2, Finished.AddMinutes(1));

        Assert.Equal(BackupStatus.Failed, failed.Status);
        Assert.Equal(0, failed.SizeBytes);
    }

    /// <summary>A running backup may not be deleted and a finished one may.</summary>
    [Fact]
    public void A_running_backup_may_not_be_deleted_and_a_finished_one_may()
    {
        Assert.False(Running().MayBeDeleted());

        var completed = Running();
        completed.Completed(2048, Digest, 2, Finished);
        Assert.True(completed.MayBeDeleted());

        var failed = Running();
        failed.Failed("BackupTruncated", Finished);
        Assert.True(failed.MayBeDeleted());
    }

    /// <summary>Retention may take a completed manual or scheduled backup, and nothing else.</summary>
    /// <remarks>
    /// <para>
    /// Every kind is named, so a member added to the enum without a decision here fails this test
    /// rather than inheriting one. The allow-list is the safe direction: a new kind is NOT prunable
    /// until somebody writes it down.
    /// </para>
    /// <para>
    /// The two exclusions are the point. A pre-restore copy is taken immediately before an operation
    /// that replaces an account, and a pre-deletion copy is the last copy of a departed customer's
    /// data in existence; a nightly pass that ate either would be destroying the safety net that
    /// justified taking it (R12).
    /// </para>
    /// </remarks>
    [Fact]
    public void Retention_may_take_a_completed_manual_or_scheduled_backup_and_nothing_else()
    {
        Assert.True(CompletedOfKind(BackupKind.Manual).MayBeRetentionPruned());
        Assert.True(CompletedOfKind(BackupKind.Scheduled).MayBeRetentionPruned());
        Assert.False(CompletedOfKind(BackupKind.PreRestore).MayBeRetentionPruned());
        Assert.False(CompletedOfKind(BackupKind.PreDeletion).MayBeRetentionPruned());

        Assert.All(
            Enum.GetValues<BackupKind>(),
            kind =>
            {
                Assert.Equal(
                    kind is BackupKind.Manual or BackupKind.Scheduled,
                    CompletedOfKind(kind).MayBeRetentionPruned());
            });
    }

    /// <summary>Retention counts and takes only completed backups.</summary>
    /// <remarks>
    /// A running row's artifact is being written at this moment, and a failed row pins nothing worth
    /// keeping. If failures counted toward the retained number, a bad week would silently expire
    /// every good copy the account still had.
    /// </remarks>
    [Fact]
    public void Retention_takes_only_completed_backups()
    {
        Assert.False(Running().MayBeRetentionPruned());

        var failed = Running();
        failed.Failed("BackupTruncated", Finished);
        Assert.False(failed.MayBeRetentionPruned());
    }

    /// <summary>A backup names no deleted account until the cascade stamps one on it.</summary>
    [Fact]
    public void A_backup_names_no_deleted_account_until_it_is_stamped()
    {
        var backup = Running();

        Assert.False(backup.NamesADeletedAccount());
        Assert.Equal(string.Empty, backup.OrphanedAccountUsername);

        backup.Orphan("cust01");

        Assert.True(backup.NamesADeletedAccount());
        Assert.Equal("cust01", backup.OrphanedAccountUsername);
    }

    /// <summary>The first stamped name survives, and an empty one is refused.</summary>
    /// <remarks>
    /// The stamp is what a deletion aims the agent with, so a second write would point it at
    /// whichever directory the name was reused for. An empty name would leave the row looking
    /// stamped and address nothing.
    /// </remarks>
    [Fact]
    public void The_first_stamped_account_name_survives_and_an_empty_one_is_refused()
    {
        var backup = Running();
        backup.Orphan(string.Empty);
        Assert.False(backup.NamesADeletedAccount());

        backup.Orphan("cust01");
        backup.Orphan("cust02");

        Assert.Equal("cust01", backup.OrphanedAccountUsername);
    }

    /// <summary>Builds a freshly started backup.</summary>
    /// <returns>A running row.</returns>
    private static Backup Running()
    {
        return new Backup(Guid.NewGuid(), Guid.NewGuid(), destinationId: null, BackupKind.Manual, Started);
    }

    /// <summary>Builds a completed backup of one kind.</summary>
    /// <param name="kind">Why the backup was taken.</param>
    /// <returns>A completed row of that kind.</returns>
    private static Backup CompletedOfKind(BackupKind kind)
    {
        var backup = new Backup(Guid.NewGuid(), Guid.NewGuid(), destinationId: null, kind, Started);
        backup.Completed(2048, Digest, 2, Finished);

        return backup;
    }
}
