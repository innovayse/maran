using Maran.Modules.Files.Models;
using Maran.Modules.Files.Services;
using Maran.Modules.Files.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Callers;

namespace Maran.Modules.Files.Tests.Services;

/// <summary>Holds <see cref="FilesAuditJournal"/> to the shape of an entry: actor, origin, subject, outcome.</summary>
public sealed class FilesAuditJournalTests
{
    /// <summary>The origin every entry is written with.</summary>
    private static readonly FilesCallOrigin Origin = new("198.51.100.4", "agent/1");

    /// <summary>An entry carries the actor, the origin, the cleaned subject and its outcome.</summary>
    [Fact]
    public async Task An_entry_names_the_actor_origin_subject_and_outcome()
    {
        var writer = new RecordingAuditWriter();
        var actor = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var journal = new FilesAuditJournal(
            writer, new FakeCurrentUser(new CustomerCaller(actor, "owner", accountId)));

        await journal.RecordSuccessAsync(
            FileAuditActions.FileRenamed, accountId, FilesAuditJournal.SubjectOf("alpha", "a/b\nc"), Origin, default);

        var entry = Assert.Single(writer.Entries);
        Assert.Equal(actor, entry.ActorUserId);
        Assert.Equal("owner", entry.ActorUsername);
        Assert.Equal(FileAuditActions.FileRenamed, entry.Action);
        Assert.Equal("alpha:a/bc", entry.Subject);
        Assert.Equal("198.51.100.4", entry.IpAddress);
        Assert.True(entry.Succeeded);
        Assert.Equal(accountId, entry.AccountId);
    }

    /// <summary>A refusal is recorded as not having taken effect.</summary>
    [Fact]
    public async Task A_refusal_is_recorded_as_not_succeeded()
    {
        var writer = new RecordingAuditWriter();
        var journal = new FilesAuditJournal(writer, new FakeCurrentUser(new AdministratorCaller(Guid.NewGuid(), "root")));

        await journal.RecordFailureAsync(FileAuditActions.FileDeleted, null, "alpha:x", Origin, default);

        Assert.False(Assert.Single(writer.Entries).Succeeded);
    }

    /// <summary>A subject is capped to what the journal column holds.</summary>
    [Fact]
    public async Task A_subject_is_capped()
    {
        var writer = new RecordingAuditWriter();
        var journal = new FilesAuditJournal(writer, new FakeCurrentUser(new AdministratorCaller(Guid.NewGuid(), "root")));

        await journal.RecordSuccessAsync(FileAuditActions.FileSaved, null, new string('p', 1000), Origin, default);

        Assert.Equal(256, writer.Entries[0].Subject.Length);
    }
}
