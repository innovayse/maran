using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Jobs;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Services;
using Maran.Modules.ServerMigration.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.ServerMigration.Tests.Jobs;

/// <summary>The hourly pass that ends the retention of an account export (issue #127, decision D11).</summary>
public sealed class AccountExportExpiryHandlerTests
{
    /// <summary>The moment the passes below run.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Everything one pass needs, over a private database.</summary>
    private sealed class World
    {
        /// <summary>The database.</summary>
        public ServerMigrationDbContext Database { get; }

        /// <summary>Removes the backups.</summary>
        public ScriptedArtifactService Artifacts { get; } = new();

        /// <summary>The audit entries.</summary>
        public RecordedAuditEntries Audit { get; } = new();

        /// <summary>The pass.</summary>
        public AccountExportExpiryHandler Handler { get; }

        /// <summary>Builds the world.</summary>
        /// <param name="name">A name for the in-memory database.</param>
        /// <param name="withBackups">Whether the panel has a Backups module.</param>
        public World(string name, bool withBackups = true)
        {
            Database = InMemoryMigrationDatabase.Open(name);
            Handler = new AccountExportExpiryHandler(
                Database,
                withBackups ? [Artifacts] : [],
                new HandlerClock { UtcNow = Now },
                new ServerMigrationAuditJournal(Audit, new AdministratorUser()),
                NullLogger<AccountExportExpiryHandler>.Instance);
        }

        /// <summary>Adds an export made <paramref name="daysAgo"/> days ago and kept for seven days.</summary>
        /// <param name="account">The account's name.</param>
        /// <param name="daysAgo">How long ago it was made.</param>
        /// <returns>The export.</returns>
        public AccountExport AddExport(string account, int daysAgo)
        {
            var export = new AccountExport(
                Guid.NewGuid(),
                Guid.NewGuid(),
                account,
                new MigrationExportArtifact(Guid.NewGuid(), 10, new string('c', 64), 0),
                "[]",
                Guid.NewGuid(),
                Now.AddDays(-daysAgo),
                TimeSpan.FromDays(7));
            Database.AccountExports.Add(export);
            Database.SaveChanges();

            return export;
        }
    }

    /// <summary>An export past its seven days loses its artifact and its record, and a fresh one keeps both.</summary>
    [Fact]
    public async Task An_expired_export_is_removed_and_a_fresh_one_is_kept()
    {
        var world = new World(nameof(An_expired_export_is_removed_and_a_fresh_one_is_kept));
        var old = world.AddExport("alice", 8);
        var fresh = world.AddExport("bob", 1);

        var removed = await world.Handler.HandleAsync(new AccountExportExpiryRequested(), CancellationToken.None);

        Assert.Equal(1, removed);
        Assert.Equal([(old.BackupId, "alice")], world.Artifacts.Discarded);
        Assert.Equal(fresh.Id, Assert.Single(world.Database.AccountExports).Id);
        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(MigrationAuditActions.ExportExpired, entry.Action);
        Assert.Equal("maran-servermigration", entry.ActorUsername);
        Assert.Null(entry.ActorUserId);
    }

    /// <summary>When the artifact cannot be removed the record stays, so the next pass tries again; the failure is journalled.</summary>
    [Fact]
    public async Task A_failed_removal_keeps_the_record_for_the_next_pass()
    {
        var world = new World(nameof(A_failed_removal_keeps_the_record_for_the_next_pass));
        world.AddExport("alice", 9);
        world.Artifacts.DiscardFailsWith = Error.Of("AgentUnavailable", ErrorType.Unavailable);

        var removed = await world.Handler.HandleAsync(new AccountExportExpiryRequested(), CancellationToken.None);

        Assert.Equal(0, removed);
        Assert.Single(world.Database.AccountExports);
        Assert.False(Assert.Single(world.Audit.Entries).Succeeded);
    }

    /// <summary>A panel with no Backups module removes nothing, because it never touched the artifacts.</summary>
    [Fact]
    public async Task A_panel_without_backups_removes_no_record()
    {
        var world = new World(nameof(A_panel_without_backups_removes_no_record), withBackups: false);
        world.AddExport("alice", 30);

        var removed = await world.Handler.HandleAsync(new AccountExportExpiryRequested(), CancellationToken.None);

        Assert.Equal(0, removed);
        Assert.Single(world.Database.AccountExports);
    }
}
