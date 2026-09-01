using Maran.Modules.Accounts.Domain;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Services;

/// <summary>
/// Behavioral contract of <see cref="AccountDirectory"/> — the one window other modules have onto
/// this module's data, and therefore the natural gap in the product's tenancy story. The calling
/// module's own query filter cannot reach across into this schema, so the scope has to be applied
/// here or not at all.
/// </summary>
public sealed class AccountDirectoryTests
{
    /// <summary>The plan every seeded account is created against.</summary>
    private static readonly Guid PlanId = Guid.Parse("22222222-0000-4000-8000-000000000001");

    /// <summary>A customer reads their own accounts snapshot.</summary>
    [Fact]
    public async Task A_customer_reads_their_own_accounts_snapshot()
    {
        await using var dbContext = CreateDbContext();
        var accountId = await SeedAsync(dbContext, "acme");

        var snapshot = await new AccountDirectory(dbContext, FakeCurrentUser.Customer(accountId))
            .FindAsync(accountId, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal("acme", snapshot.Username);
        Assert.Equal(7, snapshot.MaxSites);
        Assert.Equal(11, snapshot.MaxPhpWorkersPerPool);
    }

    /// <summary>A customer cannot read another tenants snapshot.</summary>
    [Fact]
    public async Task A_customer_cannot_read_another_tenants_snapshot()
    {
        // Without this scope, a customer could learn another tenant's SYSTEM USER NAME — the name
        // that addresses every agent operation — by guessing an account id.
        await using var dbContext = CreateDbContext();
        var theirAccountId = await SeedAsync(dbContext, "theirs");
        var myAccountId = Guid.NewGuid();

        var snapshot = await new AccountDirectory(dbContext, FakeCurrentUser.Customer(myAccountId))
            .FindAsync(theirAccountId, CancellationToken.None);

        Assert.Null(snapshot);
    }

    /// <summary>An unknown account and another tenants account are indistinguishable.</summary>
    [Fact]
    public async Task An_unknown_account_and_another_tenants_account_are_indistinguishable()
    {
        // Both answer null, so the response cannot be used to discover which account ids exist.
        await using var dbContext = CreateDbContext();
        var theirAccountId = await SeedAsync(dbContext, "theirs");
        var myAccountId = Guid.NewGuid();
        var directory = new AccountDirectory(dbContext, FakeCurrentUser.Customer(myAccountId));

        var otherTenant = await directory.FindAsync(theirAccountId, CancellationToken.None);
        var nonExistent = await directory.FindAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(otherTenant, nonExistent);
    }

    /// <summary>The directory returns the account that was asked for.</summary>
    [Fact]
    public async Task The_directory_returns_the_account_that_was_asked_for()
    {
        // Two accounts, so the identity predicate has something to get wrong. With one seeded row,
        // a lookup that ignores the id entirely still returns the right answer by accident — and
        // the caller would then create a site under, and address agent operations at, whichever
        // account happened to come back first.
        await using var dbContext = CreateDbContext();
        var first = await SeedAsync(dbContext, "first");
        var second = await SeedAsync(dbContext, "second");
        var directory = new AccountDirectory(dbContext, FakeCurrentUser.Admin());

        var firstSnapshot = await directory.FindAsync(first, CancellationToken.None);
        var secondSnapshot = await directory.FindAsync(second, CancellationToken.None);

        Assert.Equal("first", firstSnapshot!.Username);
        Assert.Equal(first, firstSnapshot.Id);
        Assert.Equal("second", secondSnapshot!.Username);
        Assert.Equal(second, secondSnapshot.Id);
    }

    /// <summary>An account that does not exist reads as nothing even for an administrator.</summary>
    [Fact]
    public async Task An_account_that_does_not_exist_reads_as_nothing_even_for_an_administrator()
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(dbContext, "acme");

        var snapshot = await new AccountDirectory(dbContext, FakeCurrentUser.Admin())
            .FindAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(snapshot);
    }

    /// <summary>An administrator reads any tenants snapshot.</summary>
    [Fact]
    public async Task An_administrator_reads_any_tenants_snapshot()
    {
        // Guards the refusal above from passing because the row was never written.
        await using var dbContext = CreateDbContext();
        var accountId = await SeedAsync(dbContext, "theirs");

        var snapshot = await new AccountDirectory(dbContext, FakeCurrentUser.Admin())
            .FindAsync(accountId, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal("theirs", snapshot.Username);
    }

    /// <summary>A principal with neither admin nor an account reads nothing.</summary>
    [Fact]
    public async Task A_principal_with_neither_admin_nor_an_account_reads_nothing()
    {
        await using var dbContext = CreateDbContext();
        var accountId = await SeedAsync(dbContext, "acme");

        var snapshot = await new AccountDirectory(dbContext, new FakeCurrentUser(Guid.NewGuid(), null, isAdmin: false))
            .FindAsync(accountId, CancellationToken.None);

        Assert.Null(snapshot);
    }

    /// <summary>Builds a fresh, isolated in-memory context.</summary>
    private static AccountsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AccountsDbContext(options);
    }

    /// <summary>Seeds one plan and one account against it, returning the account id.</summary>
    /// <param name="dbContext">The context to seed.</param>
    /// <param name="username">The account's system user name.</param>
    private static async Task<Guid> SeedAsync(AccountsDbContext dbContext, string username)
    {
        if (!await dbContext.Plans.AnyAsync(plan => plan.Id == PlanId))
        {
            dbContext.Plans.Add(new Plan(
                PlanId, "PlanStarterName", diskQuotaMb: 5_120, maxSites: 7, maxDatabases: 2, maxFtpUsers: 3,
                maxPhpWorkersPerPool: 11));
        }

        var account = new Account(
            Guid.NewGuid(),
            username,
            $"{username}.example.com",
            PlanId,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        dbContext.Accounts.Add(account);
        await dbContext.SaveChangesAsync();
        return account.Id;
    }
}
