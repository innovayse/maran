using Maran.Modules.Backups.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Backups.Tests.Persistence;

/// <summary>
/// The Backups module's tenant query filter, tested as the thing it is: ONE database holding two
/// customers' rows, and two contexts that must not see each other's (spec §8). Seeding each tenant
/// into its own database would let the setup do the separating, and the filter could then be
/// deleted with every test here still green.
/// </summary>
public sealed class BackupsDbContextTenantFilterTests
{
    /// <summary>A customer sees only their own backups.</summary>
    [Fact]
    public async Task A_customer_sees_only_their_own_backups()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var myBackup = BackupsTestContext.CompletedRow(mine);

        await using (var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.Backups.Add(myBackup);
            seeding.Backups.Add(BackupsTestContext.CompletedRow(theirs));
            await seeding.SaveChangesAsync();
        }

        await using var reading = BackupsTestContext.Create(FakeCurrentUser.Customer(mine), database);
        var visible = await reading.Backups.ToListAsync();

        Assert.Equal([myBackup.Id], visible.Select(backup =>
        {
            return backup.Id;
        }));
    }

    /// <summary>Another customers backup is not found rather than forbidden.</summary>
    /// <remarks>
    /// The refusing half. Its inverse control is
    /// <see cref="The_owning_customer_does_find_the_same_row"/>, which hands the SAME row to the
    /// account that owns it and requires it back: a filter mutated to hide everything would satisfy
    /// this test alone.
    /// </remarks>
    [Fact]
    public async Task Another_customers_backup_is_not_found_rather_than_forbidden()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var theirBackup = BackupsTestContext.CompletedRow(theirs);

        await using (var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.Backups.Add(theirBackup);
            await seeding.SaveChangesAsync();
        }

        // The vacuity guard, on the axis that goes blind: a seeding step that wrote nothing would
        // make "the row is not visible" true for the wrong reason, and every assertion below it
        // would be decoration.
        await using (var auditing = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            Assert.Equal(1, await auditing.Backups.CountAsync());
        }

        await using var reading = BackupsTestContext.Create(FakeCurrentUser.Customer(mine), database);
        var found = await reading.Backups.FirstOrDefaultAsync(backup => backup.Id == theirBackup.Id);

        // Not found, so the handler above answers 404. A 403 would confirm the row exists, which is
        // the whole of what an enumeration of backup identifiers wants (rules/security.md item 6).
        Assert.Null(found);
    }

    /// <summary>The owning customer does find the same row.</summary>
    /// <remarks>The inverse control for the refusal above: the gate must ACCEPT the valid case.</remarks>
    [Fact]
    public async Task The_owning_customer_does_find_the_same_row()
    {
        var mine = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var myBackup = BackupsTestContext.CompletedRow(mine);

        await using (var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.Backups.Add(myBackup);
            await seeding.SaveChangesAsync();
        }

        await using var reading = BackupsTestContext.Create(FakeCurrentUser.Customer(mine), database);
        var found = await reading.Backups.FirstOrDefaultAsync(backup => backup.Id == myBackup.Id);

        Assert.NotNull(found);
    }

    /// <summary>An administrator sees every accounts backups.</summary>
    [Fact]
    public async Task An_administrator_sees_every_accounts_backups()
    {
        var database = Guid.NewGuid().ToString();

        await using (var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.Backups.Add(BackupsTestContext.CompletedRow(Guid.NewGuid()));
            seeding.Backups.Add(BackupsTestContext.CompletedRow(Guid.NewGuid()));
            await seeding.SaveChangesAsync();
        }

        await using var reading = BackupsTestContext.Create(FakeCurrentUser.Admin(), database);

        Assert.Equal(2, await reading.Backups.CountAsync());
    }
}
