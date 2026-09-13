using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Seeders;
using Maran.Modules.Backups.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Backups.Tests.Seeders;

/// <summary>The startup reconciliation of the one destination every backup points at.</summary>
/// <remarks>
/// It used to copy <c>Backups__LocalRoot</c> onto the row. Nothing checked that value against the
/// directory the agent actually writes into, so on any server whose operator had set it the row —
/// and the screen reading the row — named a directory the archives were not in. The setting is gone
/// and so is the copy; what is pinned here is that the seeder writes no path and clears one it
/// finds.
/// </remarks>
public sealed class DefaultBackupDestinationSeederTests
{
    /// <summary>The instant the seeder stamps a created row with.</summary>
    private static readonly DateTimeOffset Now = new(2026, 5, 5, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A server that has never booted gains the default destination, carrying no path.</summary>
    [Fact]
    public async Task A_server_with_no_destination_gains_the_default_one()
    {
        var database = Guid.NewGuid().ToString();
        await using var dbContext = BackupsTestContext.Create(FakeCurrentUser.Admin(), database);

        await Seeder(dbContext).SeedAsync(CancellationToken.None);

        var stored = await dbContext.BackupDestinations.SingleAsync();
        Assert.Equal(BackupDestination.DefaultDestinationId, stored.Id);
        Assert.True(stored.IsDefault);
        Assert.Equal(string.Empty, stored.Path);
    }

    /// <summary>Running it twice leaves one row, because it keys on a fixed identity.</summary>
    [Fact]
    public async Task Running_it_twice_leaves_exactly_one_destination()
    {
        var database = Guid.NewGuid().ToString();
        await using var dbContext = BackupsTestContext.Create(FakeCurrentUser.Admin(), database);

        await Seeder(dbContext).SeedAsync(CancellationToken.None);
        await Seeder(dbContext).SeedAsync(CancellationToken.None);

        Assert.Equal(1, await dbContext.BackupDestinations.CountAsync());
    }

    /// <summary>A path recorded by an earlier release is cleared off the existing row.</summary>
    /// <remarks>
    /// The upgrade case, and the reason this is a reconciliation rather than a seed: a server that
    /// booted before 2026-09-08 has a copy of the removed setting in this column. Left there it is a
    /// directory nobody established, sitting in the database for an operator to read in <c>psql</c>
    /// and believe.
    /// </remarks>
    [Fact]
    public async Task A_path_recorded_by_an_earlier_release_is_cleared()
    {
        var database = Guid.NewGuid().ToString();
        await using var dbContext = BackupsTestContext.Create(FakeCurrentUser.Admin(), database);
        dbContext.BackupDestinations.Add(new BackupDestination(
            BackupDestination.DefaultDestinationId,
            "Local storage",
            BackupDestinationKind.Local,
            "/srv/backups",
            isDefault: true,
            Now));
        await dbContext.SaveChangesAsync();

        await Seeder(dbContext).SeedAsync(CancellationToken.None);

        var stored = await dbContext.BackupDestinations.SingleAsync();
        Assert.Equal(string.Empty, stored.Path);
        Assert.Equal(BackupDestination.DefaultDestinationId, stored.Id);
    }

    /// <summary>Builds the seeder over a context.</summary>
    /// <param name="dbContext">The context to reconcile against.</param>
    /// <returns>The seeder under test.</returns>
    private static DefaultBackupDestinationSeeder Seeder(
        Maran.Modules.Backups.Persistence.BackupsDbContext dbContext)
    {
        return new DefaultBackupDestinationSeeder(dbContext, new FakeClock(Now));
    }
}
