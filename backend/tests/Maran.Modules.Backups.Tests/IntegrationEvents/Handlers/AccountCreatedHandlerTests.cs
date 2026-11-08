using Maran.Modules.Backups.IntegrationEvents.Handlers;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Services;
using Maran.Modules.Backups.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Backups.Tests.IntegrationEvents.Handlers;

/// <summary>
/// What an account's creation does to this module: nothing for an account that is backed up as usual, and
/// exactly one opt-out row, journalled, for an account created without backups.
/// </summary>
public sealed class AccountCreatedHandlerTests
{
    /// <summary>The instant the fake clock reports.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Builds the creation event for an account.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="backupEnabled">Whether the creator left the account in the backup schedule.</param>
    /// <returns>The event Accounts announces.</returns>
    private static AccountCreated Created(Guid accountId, bool backupEnabled)
    {
        return new AccountCreated(
            accountId, "customer-one", "owner@example.com", "en", BackupEnabled: backupEnabled);
    }

    /// <summary>Builds the handler over a context and returns the audit double it writes to.</summary>
    /// <param name="context">The context the handler writes through.</param>
    /// <param name="audit">The audit double.</param>
    /// <returns>The handler under test.</returns>
    private static AccountCreatedHandler NewHandler(BackupsDbContext context, RecordingAuditWriter audit)
    {
        return new AccountCreatedHandler(
            context,
            new BackupAuditJournal(audit, FakeCurrentUser.Admin()),
            new FakeClock(Now));
    }

    /// <summary>An account created without backups gets one opt-out row, stamped with the clock.</summary>
    [Fact]
    public async Task An_account_created_without_backups_gets_one_opt_out_row()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var audit = new RecordingAuditWriter();

        await using (var handling = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            await NewHandler(handling, audit).HandleAsync(Created(account, backupEnabled: false), CancellationToken.None);
        }

        await using var reading = BackupsTestContext.Create(FakeCurrentUser.Admin(), database);
        var row = Assert.Single(await reading.AccountBackupOptOuts.ToListAsync());
        Assert.Equal(account, row.AccountId);
        Assert.Equal(Now, row.OptedOutAt);
    }

    /// <summary>An account created the ordinary way gets no row and no journal line.</summary>
    /// <remarks>The inverse control: a handler that always wrote the row would pass the test above.</remarks>
    [Fact]
    public async Task An_account_created_with_backups_gets_no_row_and_no_journal_line()
    {
        var database = Guid.NewGuid().ToString();
        var audit = new RecordingAuditWriter();

        await using (var handling = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            await NewHandler(handling, audit).HandleAsync(Created(Guid.NewGuid(), backupEnabled: true), CancellationToken.None);
        }

        await using var reading = BackupsTestContext.Create(FakeCurrentUser.Admin(), database);
        Assert.Empty(await reading.AccountBackupOptOuts.ToListAsync());
        Assert.Empty(audit.Entries);
    }

    /// <summary>The event's default keeps an account in the schedule.</summary>
    [Fact]
    public async Task An_event_that_says_nothing_about_backups_keeps_the_account_in_the_schedule()
    {
        var database = Guid.NewGuid().ToString();
        var older = new AccountCreated(Guid.NewGuid(), "customer-one", "owner@example.com", "en");

        await using (var handling = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            await NewHandler(handling, new RecordingAuditWriter()).HandleAsync(older, CancellationToken.None);
        }

        await using var reading = BackupsTestContext.Create(FakeCurrentUser.Admin(), database);
        Assert.Empty(await reading.AccountBackupOptOuts.ToListAsync());
    }

    /// <summary>The exclusion is journalled once, by the system, naming the account and nothing else.</summary>
    [Fact]
    public async Task The_exclusion_is_journalled_once_naming_the_account()
    {
        var audit = new RecordingAuditWriter();

        await using var handling = BackupsTestContext.Create(FakeCurrentUser.Admin());
        await NewHandler(handling, audit).HandleAsync(Created(Guid.NewGuid(), backupEnabled: false), CancellationToken.None);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(BackupAuditActions.BackupOptedOut, entry.Action);
        Assert.Equal("customer-one", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>The row is written whichever principal creates the account, a reseller included.</summary>
    /// <remarks>
    /// A reseller creating a customer's account is the case the tenant filter hides every row from, so a
    /// handler that read before it wrote would see nothing and still be right only by accident.
    /// </remarks>
    [Fact]
    public async Task The_row_is_written_whichever_principal_creates_the_account()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();

        await using (var handling = BackupsTestContext.Create(FakeCurrentUser.Reseller(Guid.NewGuid()), database))
        {
            await NewHandler(handling, new RecordingAuditWriter())
                .HandleAsync(Created(account, backupEnabled: false), CancellationToken.None);
        }

        await using var reading = BackupsTestContext.Create(FakeCurrentUser.Admin(), database);
        Assert.Equal(account, Assert.Single(await reading.AccountBackupOptOuts.ToListAsync()).AccountId);
    }

    /// <summary>A repeated delivery that collides on the key is a quiet no-op: no second row, no second journal line.</summary>
    [Fact]
    public async Task A_repeated_delivery_that_collides_on_the_key_is_a_quiet_no_op()
    {
        var account = Guid.NewGuid();
        var audit = new RecordingAuditWriter();
        var options = new DbContextOptionsBuilder<BackupsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new UniqueViolationOnSaveInterceptor())
            .Options;

        await using var context = new BackupsDbContext(options, FakeCurrentUser.Admin(), new MarkingEncryptionService());
        await NewHandler(context, audit).HandleAsync(Created(account, backupEnabled: false), CancellationToken.None);

        Assert.Empty(audit.Entries);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
