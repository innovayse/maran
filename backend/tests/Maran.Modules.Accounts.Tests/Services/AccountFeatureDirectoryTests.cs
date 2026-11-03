using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Services;

/// <summary>
/// Behavioral contract of <see cref="AccountFeatureDirectory"/>: the window the plan-feature filter
/// asks through. Its answer must follow the plan, and must disclose nothing about another tenant.
/// </summary>
public sealed class AccountFeatureDirectoryTests
{
    /// <summary>A customer is told yes for a feature their plan includes and no for one it lacks.</summary>
    [Fact]
    public async Task A_customer_is_told_yes_for_an_included_feature_and_no_for_a_missing_one()
    {
        await using var dbContext = CreateDbContext();
        var accountId = await SeedAsync(dbContext, "acme", AccountFeature.Databases | AccountFeature.Cron);
        var directory = new AccountFeatureDirectory(dbContext, FakeCurrentUser.Customer(accountId));

        Assert.True(await directory.IncludesAsync(accountId, AccountFeature.Databases, CancellationToken.None));
        Assert.False(await directory.IncludesAsync(accountId, AccountFeature.Backups, CancellationToken.None));
        Assert.False(await directory.IncludesAsync(
            accountId, AccountFeature.Databases | AccountFeature.Backups, CancellationToken.None));
    }

    /// <summary>An edit of the plan is visible on the very next question, because nothing is cached.</summary>
    [Fact]
    public async Task An_edit_of_the_plan_is_visible_on_the_next_question()
    {
        await using var dbContext = CreateDbContext();
        var accountId = await SeedAsync(dbContext, "acme", AccountFeature.All);
        var directory = new AccountFeatureDirectory(dbContext, FakeCurrentUser.Customer(accountId));
        Assert.True(await directory.IncludesAsync(accountId, AccountFeature.Mail, CancellationToken.None));

        var plan = await dbContext.Plans.SingleAsync();
        plan.ChangeFeatures(AccountFeature.Dns);
        await dbContext.SaveChangesAsync();

        Assert.False(await directory.IncludesAsync(accountId, AccountFeature.Mail, CancellationToken.None));
    }

    /// <summary>Another tenant's account and an unknown account both read as no, so nothing is disclosed.</summary>
    [Fact]
    public async Task Another_tenants_account_and_an_unknown_account_both_read_as_no()
    {
        await using var dbContext = CreateDbContext();
        var theirs = await SeedAsync(dbContext, "theirs", AccountFeature.All);
        var directory = new AccountFeatureDirectory(dbContext, FakeCurrentUser.Customer(Guid.NewGuid()));

        Assert.False(await directory.IncludesAsync(theirs, AccountFeature.Databases, CancellationToken.None));
        Assert.False(await directory.IncludesAsync(Guid.NewGuid(), AccountFeature.Databases, CancellationToken.None));
    }

    /// <summary>An administrator may ask about any account, and an unknown one is still no.</summary>
    [Fact]
    public async Task An_administrator_may_ask_about_any_account()
    {
        await using var dbContext = CreateDbContext();
        var accountId = await SeedAsync(dbContext, "acme", AccountFeature.Cron);
        var directory = new AccountFeatureDirectory(dbContext, FakeCurrentUser.Admin());

        Assert.True(await directory.IncludesAsync(accountId, AccountFeature.Cron, CancellationToken.None));
        Assert.False(await directory.IncludesAsync(accountId, AccountFeature.Mail, CancellationToken.None));
        Assert.False(await directory.IncludesAsync(Guid.NewGuid(), AccountFeature.Cron, CancellationToken.None));
    }

    /// <summary>Builds a fresh, isolated in-memory context.</summary>
    private static AccountsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AccountsDbContext(options, FakeCurrentUser.Admin());
    }

    /// <summary>Seeds one plan with the given features and one account on it, returning the account id.</summary>
    /// <param name="dbContext">The context to seed.</param>
    /// <param name="username">The account's system user name.</param>
    /// <param name="features">The features the account's plan includes.</param>
    private static async Task<Guid> SeedAsync(AccountsDbContext dbContext, string username, AccountFeature features)
    {
        var plan = Plan.Define(Guid.NewGuid(), $"Plan {username}", 5_120, 7, 2, 3, 0, 13, 11, features);
        dbContext.Plans.Add(plan);
        var account = new Account(
            Guid.NewGuid(),
            username,
            $"{username}.example.com",
            plan.Id,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        dbContext.Accounts.Add(account);
        await dbContext.SaveChangesAsync();
        return account.Id;
    }
}
