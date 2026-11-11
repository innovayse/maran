using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Services;

/// <summary>
/// Behavioral contract of <see cref="AccountDirectory.FindSystemUserAsync"/>: the unscoped window
/// unattended certificate renewal reads an account's system user name through (issue #379), run over
/// the real Accounts context whose fail-closed filter hides every row from an anonymous caller.
/// </summary>
/// <remarks>
/// Every test seeds through an administrator's context and reads through a context built for the
/// caller under test, on the same named in-memory database, so the filter is the real one. A stub
/// directory that ignores scope could not observe the difference this class exists to pin down.
/// </remarks>
public sealed class AccountDirectorySystemUserTests
{
    /// <summary>The plan every seeded account is created against.</summary>
    private static readonly Guid PlanId = Guid.Parse("22222222-0000-4000-8000-000000000002");

    /// <summary>An anonymous caller reads the system user name of an account that exists.</summary>
    [Fact]
    public async Task An_anonymous_caller_reads_the_system_user_name_of_an_account_that_exists()
    {
        var database = Guid.NewGuid().ToString();
        var accountId = await SeedAsync(database, "acme");
        var anonymous = FakeCurrentUser.Anonymous();
        await using var dbContext = AccountsTestContext.CreateOn(database, anonymous);

        var user = await new AccountDirectory(dbContext, anonymous)
            .FindSystemUserAsync(accountId, CancellationToken.None);

        Assert.NotNull(user);
        Assert.Equal(accountId, user.Id);
        Assert.Equal("acme", user.Username);
    }

    /// <summary>The scoped lookup still answers null for the same anonymous caller and the same account.</summary>
    /// <remarks>
    /// The inverse control: it proves the account is really there (the test above) and that
    /// <see cref="AccountDirectory.FindAsync"/> was not widened to make the new window look right.
    /// </remarks>
    [Fact]
    public async Task The_scoped_lookup_still_answers_null_for_the_same_anonymous_caller_and_account()
    {
        var database = Guid.NewGuid().ToString();
        var accountId = await SeedAsync(database, "acme");
        var anonymous = FakeCurrentUser.Anonymous();
        await using var dbContext = AccountsTestContext.CreateOn(database, anonymous);
        var directory = new AccountDirectory(dbContext, anonymous);

        Assert.Null(await directory.FindAsync(accountId, CancellationToken.None));
        Assert.NotNull(await directory.FindSystemUserAsync(accountId, CancellationToken.None));
    }

    /// <summary>An id no account has answers null.</summary>
    [Fact]
    public async Task An_unknown_account_id_answers_null()
    {
        var database = Guid.NewGuid().ToString();
        await SeedAsync(database, "acme");
        var anonymous = FakeCurrentUser.Anonymous();
        await using var dbContext = AccountsTestContext.CreateOn(database, anonymous);

        var user = await new AccountDirectory(dbContext, anonymous)
            .FindSystemUserAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(user);
    }

    /// <summary>A customer of another account still reads the name, because the window ignores scope for every caller.</summary>
    [Fact]
    public async Task A_customer_of_another_account_still_reads_the_name_because_the_window_ignores_scope()
    {
        var database = Guid.NewGuid().ToString();
        var theirs = await SeedAsync(database, "theirs");
        var mine = await SeedAsync(database, "mine");
        var customer = FakeCurrentUser.Customer(mine);
        await using var dbContext = AccountsTestContext.CreateOn(database, customer);
        var directory = new AccountDirectory(dbContext, customer);

        var user = await directory.FindSystemUserAsync(theirs, CancellationToken.None);

        Assert.NotNull(user);
        Assert.Equal("theirs", user.Username);
        Assert.Null(await directory.FindAsync(theirs, CancellationToken.None));
    }

    /// <summary>The system lookup finds the account with its plan figures for a caller whose scope hides it.</summary>
    [Fact]
    public async Task The_system_lookup_finds_the_account_with_its_plan_for_a_caller_the_scope_hides_it_from()
    {
        var database = Guid.NewGuid().ToString();
        var accountId = await SeedAsync(database, "acme");
        var anonymous = FakeCurrentUser.Anonymous();
        await using var dbContext = AccountsTestContext.CreateOn(database, anonymous);
        var directory = new AccountDirectory(dbContext, anonymous);

        var snapshot = await directory.FindForSystemAsync(accountId, CancellationToken.None);

        Assert.Null(await directory.FindAsync(accountId, CancellationToken.None));
        Assert.NotNull(snapshot);
        Assert.Equal("acme", snapshot.Username);
        Assert.Equal(7, snapshot.MaxSites);
    }

    /// <summary>The system lookup answers null for an id no account has.</summary>
    [Fact]
    public async Task The_system_lookup_answers_null_for_an_unknown_id()
    {
        var database = Guid.NewGuid().ToString();
        await SeedAsync(database, "acme");
        var anonymous = FakeCurrentUser.Anonymous();
        await using var dbContext = AccountsTestContext.CreateOn(database, anonymous);

        var snapshot = await new AccountDirectory(dbContext, anonymous)
            .FindForSystemAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(snapshot);
    }

    /// <summary>Seeds one plan and one account on the named database as an administrator.</summary>
    /// <param name="database">The in-memory database name shared with the reading context.</param>
    /// <param name="username">The account's system user name.</param>
    /// <returns>The new account's id.</returns>
    private static async Task<Guid> SeedAsync(string database, string username)
    {
        await using var seed = AccountsTestContext.CreateOn(database, FakeCurrentUser.Admin());
        if (!await seed.Plans.AnyAsync(plan => plan.Id == PlanId))
        {
            seed.Plans.Add(new Plan(
                PlanId, "PlanStarterName", diskQuotaMb: 5_120, maxSites: 7, maxDatabases: 2, maxSftpUsers: 3,
                maxCronEntries: 13, maxPhpWorkersPerPool: 11, maxFtpUsers: 17));
        }

        var account = new Account(
            Guid.NewGuid(),
            username,
            $"{username}.example.com",
            PlanId,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        seed.Accounts.Add(account);
        await seed.SaveChangesAsync();
        return account.Id;
    }
}
