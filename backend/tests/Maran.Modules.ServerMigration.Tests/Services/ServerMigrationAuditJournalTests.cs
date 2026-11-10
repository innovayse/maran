using Maran.Modules.ServerMigration.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.ServerMigration.Tests.Services;

/// <summary>What an entry of the remote-server journal carries: who, what, and never a key.</summary>
public sealed class ServerMigrationAuditJournalTests
{
    /// <summary>A success is recorded under the signed-in administrator, with the caller's address and agent.</summary>
    [Fact]
    public async Task A_success_is_recorded_under_the_signed_in_administrator()
    {
        var writer = new Writer();
        var userId = Guid.NewGuid();
        var journal = new ServerMigrationAuditJournal(writer, new User(new AdministratorCaller(userId, "root-admin")));

        await journal.RecordSuccessAsync(
            MigrationAuditActions.RemoteServerAdded, "old.example.net (1)", "203.0.113.9", "agent/1", CancellationToken.None);

        var entry = Assert.Single(writer.Entries);
        Assert.Equal(userId, entry.ActorUserId);
        Assert.Equal("root-admin", entry.ActorUsername);
        Assert.Equal("RemoteServerAdded", entry.Action);
        Assert.Equal("old.example.net (1)", entry.Subject);
        Assert.Equal("203.0.113.9", entry.IpAddress);
        Assert.Equal("agent/1", entry.UserAgent);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A refusal is recorded too, marked as not having taken effect.</summary>
    [Fact]
    public async Task A_refusal_is_recorded_as_not_succeeded()
    {
        var writer = new Writer();
        var journal = new ServerMigrationAuditJournal(writer, new User(new AdministratorCaller(Guid.NewGuid(), "admin")));

        await journal.RecordFailureAsync(
            MigrationAuditActions.RemoteServerHostKeyPinned, "old.example.net (1)", "203.0.113.9", "agent/1", CancellationToken.None);

        var entry = Assert.Single(writer.Entries);
        Assert.False(entry.Succeeded);
        Assert.Equal("RemoteServerHostKeyPinned", entry.Action);
    }

    /// <summary>An unauthenticated caller leaves an entry with no actor rather than throwing.</summary>
    [Fact]
    public async Task An_anonymous_caller_leaves_an_entry_with_no_actor()
    {
        var writer = new Writer();
        var journal = new ServerMigrationAuditJournal(writer, new User(AnonymousCaller.Instance));

        await journal.RecordFailureAsync(MigrationAuditActions.RemoteServerTested, "x", "ip", "ua", CancellationToken.None);

        var entry = Assert.Single(writer.Entries);
        Assert.Null(entry.ActorUserId);
        Assert.Equal(string.Empty, entry.ActorUsername);
    }

    /// <summary>The module's actions are the six it audits, each a distinct stable name.</summary>
    [Fact]
    public void The_actions_are_six_distinct_names()
    {
        string[] actions =
        [
            MigrationAuditActions.RemoteServerAdded,
            MigrationAuditActions.RemoteServerHostKeyPinned,
            MigrationAuditActions.RemoteServerTested,
            MigrationAuditActions.RemoteServerEdited,
            MigrationAuditActions.RemoteServerRemoved,
            MigrationAuditActions.RemoteServerDiscovered,
        ];

        Assert.Equal(6, actions.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>An <see cref="IAuditWriter"/> that keeps every entry in memory.</summary>
    private sealed class Writer : IAuditWriter
    {
        /// <summary>Every entry written, in order.</summary>
        private readonly List<AuditEntry> _entries = [];

        /// <summary>Every entry written, in order.</summary>
        public IReadOnlyList<AuditEntry> Entries
        {
            get { return _entries; }
        }

        /// <inheritdoc />
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
        {
            _entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    /// <summary>An <see cref="ICurrentUser"/> for a fixed caller.</summary>
    private sealed class User : ICurrentUser
    {
        /// <summary>Creates the double.</summary>
        /// <param name="caller">Who the request runs as.</param>
        public User(Caller caller)
        {
            Caller = caller;
        }

        /// <inheritdoc />
        public Caller Caller { get; }
    }
}
