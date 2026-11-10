using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;
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

        // The cron allowance travels with the snapshot because the Cron module keeps no rows of its
        // own and cannot read the plan any other way. The seeded value is deliberately unlike every
        // other number on the plan, so a projection that dropped it — or picked up its neighbour —
        // fails here rather than showing up as a limit that refuses the wrong customer.
        Assert.Equal(13, snapshot.MaxCronEntries);

        // The FTPS allowance travels for the same reason and is a SEPARATE number from the SFTP one:
        // 17 is unlike every other integer on this plan, so a projection that dropped it reads 0 —
        // the record's default — and a projection that picked up its neighbour reads 3 or 11.
        Assert.Equal(17, snapshot.MaxFtpUsers);
    }

    /// <summary>Both directory reads state whether the account is active or suspended.</summary>
    [Fact]
    public async Task Both_directory_reads_state_whether_the_account_is_active_or_suspended()
    {
        await using var dbContext = CreateDbContext();
        var activeId = await SeedAsync(dbContext, "acme");
        var suspendedId = await SeedAsync(dbContext, "paused");
        (await dbContext.Accounts.SingleAsync(account => account.Id == suspendedId)).Suspend(SuspensionActor.Administrator);
        await dbContext.SaveChangesAsync();
        var directory = new AccountDirectory(dbContext, FakeCurrentUser.Admin());

        Assert.Equal(AccountState.Active, (await directory.FindAsync(activeId, CancellationToken.None))!.State);
        Assert.Equal(AccountState.Suspended, (await directory.FindAsync(suspendedId, CancellationToken.None))!.State);
        var listed = await directory.ListAsync(CancellationToken.None);
        Assert.Equal(AccountState.Suspended, Assert.Single(listed, snapshot =>
        {
            return snapshot.Id == suspendedId;
        }).State);
        Assert.Equal(AccountState.Active, Assert.Single(listed, snapshot =>
        {
            return snapshot.Id == activeId;
        }).State);
    }

    /// <summary>Every account in the host wide list carries its plans ftps allowance.</summary>
    /// <remarks>
    /// The second production construction site of <see cref="Maran.Sdk.Contracts.AccountSnapshot"/>,
    /// and it needs its own assertion: the record's new member carries a default, so a list projection
    /// that simply omits it compiles, runs, and answers zero for every account on the host.
    /// </remarks>
    [Fact]
    public async Task Every_account_in_the_host_wide_list_carries_its_plans_ftps_allowance()
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(dbContext, "acme");

        var snapshots = await new AccountDirectory(dbContext, FakeCurrentUser.Admin())
            .ListAsync(CancellationToken.None);

        var snapshot = Assert.Single(snapshots);
        Assert.Equal("acme", snapshot.Username);
        Assert.Equal(17, snapshot.MaxFtpUsers);
    }

    /// <summary>A customer cannot read the snapshot of another tenant.</summary>
    [Fact]
    public async Task A_customer_cannot_read_the_snapshot_of_another_tenant()
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

    /// <summary>An unknown account and the account of another tenant are indistinguishable.</summary>
    [Fact]
    public async Task An_unknown_account_and_the_account_of_another_tenant_are_indistinguishable()
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

    /// <summary>
    /// The host-wide list answers every account with its own plan's figures to every kind of caller,
    /// an anonymous one included — the unattended runs (backup, retention, PHP reconcile) that use it
    /// have no principal, and the context's filters would show such a caller nothing.
    /// </summary>
    /// <remarks>
    /// One account is owned by a reseller on that reseller's plan and one by the administrator on the
    /// administrator's, so a list that bypassed the account filter but not the plan filter (or the
    /// reverse) loses a row or a plan figure here. The control reads the very same database through an
    /// anonymous context and finds neither table's rows, so the list is the bypass and not an open filter.
    /// </remarks>
    [Fact]
    public async Task The_host_wide_list_answers_every_account_with_its_plan_to_every_kind_of_caller()
    {
        var database = Guid.NewGuid().ToString();
        var reseller = ResellerTestData.Reseller("agency");
        await using (var seed = AccountsTestContext.CreateOn(database, FakeCurrentUser.Admin()))
        {
            var resoldPlan = Plan.Define(
                Guid.NewGuid(), "Resold", 1_000, 7, 2, 3, 17, 13, 11, resellerId: reseller.Id);
            var directPlan = Plan.Define(Guid.NewGuid(), "Direct", 2_000, 9, 2, 3, 17, 13, 11);
            seed.Resellers.Add(reseller);
            seed.Plans.AddRange(resoldPlan, directPlan);
            seed.Accounts.Add(new Account(
                Guid.NewGuid(), "resold", "resold.example.com", resoldPlan.Id, ResellerTestData.CreatedAt,
                resellerId: reseller.Id));
            seed.Accounts.Add(new Account(
                Guid.NewGuid(), "direct", "direct.example.com", directPlan.Id, ResellerTestData.CreatedAt));
            await seed.SaveChangesAsync();
        }

        await using (var anonymous = AccountsTestContext.CreateOn(database, FakeCurrentUser.Anonymous()))
        {
            Assert.Empty(await anonymous.Accounts.ToListAsync());
            Assert.Empty(await anonymous.Plans.ToListAsync());
        }

        ICurrentUser[] callers =
        [
            FakeCurrentUser.Anonymous(),
            FakeCurrentUser.AccountlessCustomer(),
            FakeCurrentUser.Customer(Guid.NewGuid()),
            FakeCurrentUser.Reseller(Guid.NewGuid()),
            FakeCurrentUser.Admin(),
        ];
        foreach (var caller in callers)
        {
            await using var dbContext = AccountsTestContext.CreateOn(database, caller);

            var snapshots = await new AccountDirectory(dbContext, caller).ListAsync(CancellationToken.None);

            Assert.Equal(2, snapshots.Count);
            Assert.Equal(7, Assert.Single(snapshots, snapshot =>
            {
                return snapshot.Username == "resold";
            }).MaxSites);
            Assert.Equal(9, Assert.Single(snapshots, snapshot =>
            {
                return snapshot.Username == "direct";
            }).MaxSites);
        }
    }

    /// <summary>A customer reads their own snapshot, plan figures included, through their own context.</summary>
    /// <remarks>
    /// The directory and the context are built for the same customer, as in production, so the
    /// snapshot's join onto the plan passes through the customer's plan filter: a filter that hid the
    /// customer's own plan would turn this answer into null.
    /// </remarks>
    [Fact]
    public async Task A_customer_reads_their_own_snapshot_through_a_context_filtered_for_them()
    {
        var database = Guid.NewGuid().ToString();
        Guid accountId;
        await using (var seed = AccountsTestContext.CreateOn(database, FakeCurrentUser.Admin()))
        {
            accountId = await SeedAsync(seed, "acme");
            await SeedAsync(seed, "theirs");
        }

        var customer = FakeCurrentUser.Customer(accountId);
        await using var dbContext = AccountsTestContext.CreateOn(database, customer);

        var snapshot = await new AccountDirectory(dbContext, customer).FindAsync(accountId, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal("acme", snapshot.Username);
        Assert.Equal(7, snapshot.MaxSites);
        Assert.Equal(17, snapshot.MaxFtpUsers);
    }

    /// <summary>A customer cannot read the snapshot of another tenant when the context is built for them.</summary>
    /// <remarks>
    /// What is observed here is the directory's own scope check: it refuses an id other than the
    /// customer's account before it reads the context, so the answer would be the same with the
    /// context's query filter removed. The filter itself is proved in
    /// <c>AccountsDbContextFailClosedTests</c>. The tests earlier in this class read through an
    /// administrator's context; this one builds the context for the customer, as in production, with
    /// the customer's own account read in the same context as the positive control.
    /// </remarks>
    [Fact]
    public async Task A_customer_cannot_read_the_snapshot_of_another_tenant_when_the_context_is_built_for_them()
    {
        var database = Guid.NewGuid().ToString();
        Guid mine;
        Guid theirs;
        await using (var seed = AccountsTestContext.CreateOn(database, FakeCurrentUser.Admin()))
        {
            mine = await SeedAsync(seed, "mine");
            theirs = await SeedAsync(seed, "theirs");
        }

        var customer = FakeCurrentUser.Customer(mine);
        await using var dbContext = AccountsTestContext.CreateOn(database, customer);
        var directory = new AccountDirectory(dbContext, customer);

        Assert.Null(await directory.FindAsync(theirs, CancellationToken.None));
        Assert.NotNull(await directory.FindAsync(mine, CancellationToken.None));
    }

    /// <summary>
    /// Builds a fresh, isolated in-memory context for an administrator, which no query filter narrows —
    /// so the tests reading through it observe the directory's own comparison as the only defence.
    /// </summary>
    private static AccountsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AccountsDbContext(options, FakeCurrentUser.Admin());
    }

    /// <summary>Seeds one plan and one account against it, returning the account id.</summary>
    /// <param name="dbContext">The context to seed.</param>
    /// <param name="username">The account's system user name.</param>
    private static async Task<Guid> SeedAsync(AccountsDbContext dbContext, string username)
    {
        if (!await dbContext.Plans.AnyAsync(plan => plan.Id == PlanId))
        {
            dbContext.Plans.Add(new Plan(
                PlanId, "PlanStarterName", diskQuotaMb: 5_120, maxSites: 7, maxDatabases: 2, maxSftpUsers: 3,
                maxCronEntries: 13, maxPhpWorkersPerPool: 11, maxFtpUsers: 17));
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
