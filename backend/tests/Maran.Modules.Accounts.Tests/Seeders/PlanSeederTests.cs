using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Seeders;
using Maran.Modules.Accounts.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Seeders;

/// <summary>
/// The standard plans a fresh installation ships with. Their limits are not decoration: a plan's
/// per-pool worker budget becomes <c>pm.max_children</c> in every rendered php-fpm pool, so a wrong
/// or absent value here is either a site that cannot start or a customer handed the whole server.
/// </summary>
public sealed class PlanSeederTests
{
    /// <summary>Seeding inserts the three standard plans.</summary>
    [Fact]
    public async Task Seeding_inserts_the_three_standard_plans()
    {
        await using var dbContext = CreateDbContext();

        await new PlanSeeder(dbContext).SeedAsync(CancellationToken.None);

        Assert.Equal(3, await dbContext.Plans.CountAsync());
    }

    /// <summary>Seeding twice inserts nothing the second time.</summary>
    [Fact]
    public async Task Seeding_twice_inserts_nothing_the_second_time()
    {
        await using var dbContext = CreateDbContext();
        var seeder = new PlanSeeder(dbContext);

        await seeder.SeedAsync(CancellationToken.None);
        await seeder.SeedAsync(CancellationToken.None);

        Assert.Equal(3, await dbContext.Plans.CountAsync());
    }

    /// <summary>Each standard plan carries the site and worker limits it is documented with.</summary>
    [Theory]
    [InlineData("11111111-0000-4000-8000-000000000001", 5, 5)]
    [InlineData("11111111-0000-4000-8000-000000000002", 25, 10)]
    [InlineData("11111111-0000-4000-8000-000000000003", 500, 20)]
    public async Task Each_standard_plan_carries_the_site_and_worker_limits_it_is_documented_with(
        string planId,
        int maxSites,
        int maxPhpWorkersPerPool)
    {
        await AssertLimitsAsync(planId, maxSites, maxPhpWorkersPerPool);
    }

    /// <summary>Each standard plan carries the database allowance it is documented with.</summary>
    [Theory]
    [InlineData("11111111-0000-4000-8000-000000000001", 2)]
    [InlineData("11111111-0000-4000-8000-000000000002", 10)]
    [InlineData("11111111-0000-4000-8000-000000000003", 500)]
    public async Task Each_standard_plan_carries_the_database_allowance_it_is_documented_with(
        string planId,
        int maxDatabases)
    {
        // Pinned for the same reason the site and worker limits are, and with one extra: this is the
        // number the Databases module refuses a creation against, so a customer's whole experience of
        // "how many databases do I get" is these three values. The column has held them since the
        // initial Accounts schema, so no backfill migration is needed — the failure mode the previous
        // plan hit (a migration adding a limit column with a zero default while the seeder only
        // inserts ABSENT plans, leaving every existing installation at zero) does not arise here, and
        // Plan's constructor now refuses a non-positive allowance outright.
        await using var dbContext = CreateDbContext();
        await new PlanSeeder(dbContext).SeedAsync(CancellationToken.None);

        var plan = await dbContext.Plans.SingleAsync(p => p.Id == Guid.Parse(planId));

        Assert.Equal(maxDatabases, plan.MaxDatabases);
    }

    /// <summary>Each standard plan carries the sftp allowance it is documented with.</summary>
    [Theory]
    [InlineData("11111111-0000-4000-8000-000000000001", 3)]
    [InlineData("11111111-0000-4000-8000-000000000002", 10)]
    [InlineData("11111111-0000-4000-8000-000000000003", 100)]
    public async Task Each_standard_plan_carries_the_sftp_allowance_it_is_documented_with(
        string planId,
        int maxSftpUsers)
    {
        // These are the values the column has held since the initial Accounts schema, under its old
        // name MaxFtpUsers. The Sftp module's arrival RENAMED that column rather than adding one
        // beside it — this panel serves file transfer over SFTP alone, so a separate FTP count would
        // be a second limit on one thing — and the rename migration carries every seeded and
        // operator-edited value across untouched. Pinned here so the rename is provably value-
        // preserving rather than merely believed to be.
        await using var dbContext = CreateDbContext();
        await new PlanSeeder(dbContext).SeedAsync(CancellationToken.None);

        var plan = await dbContext.Plans.SingleAsync(p => p.Id == Guid.Parse(planId));

        Assert.Equal(maxSftpUsers, plan.MaxSftpUsers);
    }

    /// <summary>Each standard plan carries the ftps allowance it is documented with.</summary>
    [Theory]
    [InlineData("11111111-0000-4000-8000-000000000001", 3)]
    [InlineData("11111111-0000-4000-8000-000000000002", 10)]
    [InlineData("11111111-0000-4000-8000-000000000003", 100)]
    public async Task Each_standard_plan_carries_the_ftps_allowance_it_is_documented_with(
        string planId,
        int maxFtpUsers)
    {
        // Pinned because these are the values AddPlanMaxFtpUsers backfills onto every existing
        // installation, and the two must not drift: a seeder and a migration that disagree hand two
        // customers on the same tier different allowances depending on when their server was built.
        // The numbers equal each tier's SFTP allowance by decision, not by derivation — the entity
        // keeps them separate — so this theory is also what would catch a "fix" that read one from
        // the other.
        await using var dbContext = CreateDbContext();
        await new PlanSeeder(dbContext).SeedAsync(CancellationToken.None);

        var plan = await dbContext.Plans.SingleAsync(p => p.Id == Guid.Parse(planId));

        Assert.Equal(maxFtpUsers, plan.MaxFtpUsers);
    }

    /// <summary>Asserts one seeded plan's site and worker limits.</summary>
    /// <param name="planId">The plan's fixed identity.</param>
    /// <param name="maxSites">The site allowance it must carry.</param>
    /// <param name="maxPhpWorkersPerPool">The per-pool worker budget it must carry.</param>
    private static async Task AssertLimitsAsync(string planId, int maxSites, int maxPhpWorkersPerPool)
    {
        // Pinned to exact numbers deliberately. These are the values the migration backfills onto
        // every existing installation, so the two must not drift apart, and they are the numbers a
        // reviewer was asked to sign off — a limit nothing asserts is a limit nobody agreed to.
        await using var dbContext = CreateDbContext();
        await new PlanSeeder(dbContext).SeedAsync(CancellationToken.None);

        var plan = await dbContext.Plans.SingleAsync(p => p.Id == Guid.Parse(planId));

        Assert.Equal(maxSites, plan.MaxSites);
        Assert.Equal(maxPhpWorkersPerPool, plan.MaxPhpWorkersPerPool);
    }

    /// <summary>Every seeded plan has a positive worker budget.</summary>
    [Fact]
    public async Task Every_seeded_plan_has_a_positive_worker_budget()
    {
        // php-fpm refuses to start a pool with pm.max_children <= 0, so a zero here is a plan that
        // cannot serve PHP at all.
        await using var dbContext = CreateDbContext();
        await new PlanSeeder(dbContext).SeedAsync(CancellationToken.None);

        Assert.All(await dbContext.Plans.ToListAsync(), plan =>
        {
            Assert.True(plan.MaxPhpWorkersPerPool > 0);
        });
    }

    /// <summary>Builds a fresh, isolated in-memory context.</summary>
    private static AccountsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AccountsDbContext(options, FakeCurrentUser.Admin());
    }

    /// <summary>Seeding does not bring back a standard plan an operator deleted.</summary>
    [Fact]
    public async Task Seeding_does_not_bring_back_a_standard_plan_an_operator_deleted()
    {
        // Deleting a plan became possible with plan management. Before, the seeder topped up absent
        // plans on every start, which would now undo a deliberate deletion at the next restart.
        await using var dbContext = CreateDbContext();
        var seeder = new PlanSeeder(dbContext);
        await seeder.SeedAsync(CancellationToken.None);
        dbContext.Plans.Remove(await dbContext.Plans.SingleAsync(p => p.Id == PlanSeeder.StarterPlanId));
        await dbContext.SaveChangesAsync();

        await seeder.SeedAsync(CancellationToken.None);

        Assert.Equal(2, await dbContext.Plans.CountAsync());
        Assert.DoesNotContain(await dbContext.Plans.ToListAsync(), plan =>
        {
            return plan.Id == PlanSeeder.StarterPlanId;
        });
    }

    /// <summary>Seeding adds nothing to a panel that already has a plan of the operator's own.</summary>
    [Fact]
    public async Task Seeding_adds_nothing_to_a_panel_that_already_has_a_plan_of_the_operators_own()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Plans.Add(Plan.Define(Guid.NewGuid(), "Agency", 10_240, 10, 5, 4, 2, 3, 8));
        await dbContext.SaveChangesAsync();

        await new PlanSeeder(dbContext).SeedAsync(CancellationToken.None);

        Assert.Equal("Agency", Assert.Single(await dbContext.Plans.ToListAsync()).Name);
    }

    /// <summary>Seeding as the anonymous start-up caller inserts the three standard plans.</summary>
    /// <remarks>
    /// The seeder runs at boot with no principal, and the context shows an anonymous caller no plan.
    /// The plans are written through that context and counted through an administrator's over the
    /// same database, so the count is the stored rows and not what the anonymous filter lets through.
    /// </remarks>
    [Fact]
    public async Task Seeding_as_the_anonymous_start_up_caller_inserts_the_three_standard_plans()
    {
        var database = Guid.NewGuid().ToString();

        await using (var startUp = AccountsTestContext.CreateOn(database, FakeCurrentUser.Anonymous()))
        {
            await new PlanSeeder(startUp).SeedAsync(CancellationToken.None);
        }

        await using var reader = AccountsTestContext.CreateOn(database, FakeCurrentUser.Admin());
        Assert.Equal(3, await reader.Plans.CountAsync());
    }

    /// <summary>A second start-up as the anonymous caller finds the stored plans and inserts nothing.</summary>
    /// <remarks>
    /// The existence check must see the stored plans although the anonymous filter hides every one:
    /// a filtered check answers "none" on every boot and the insert collides with the fixed ids. Each
    /// start-up gets its own context and seeder, as each process start does.
    /// </remarks>
    [Fact]
    public async Task A_second_start_up_as_the_anonymous_caller_inserts_nothing()
    {
        var database = Guid.NewGuid().ToString();
        for (var start = 0; start < 2; start++)
        {
            await using var startUp = AccountsTestContext.CreateOn(database, FakeCurrentUser.Anonymous());

            await new PlanSeeder(startUp).SeedAsync(CancellationToken.None);
        }

        await using var reader = AccountsTestContext.CreateOn(database, FakeCurrentUser.Admin());
        Assert.Equal(3, await reader.Plans.CountAsync());
    }

    /// <summary>A start-up as the anonymous caller adds nothing to a panel that already has a plan.</summary>
    /// <remarks>
    /// The plan belongs to a reseller, the case an owner-scoped or filtered existence check would miss.
    /// </remarks>
    [Fact]
    public async Task A_start_up_as_the_anonymous_caller_adds_nothing_to_a_panel_that_already_has_a_plan()
    {
        var database = Guid.NewGuid().ToString();
        await using (var seed = AccountsTestContext.CreateOn(database, FakeCurrentUser.Admin()))
        {
            seed.Plans.Add(Plan.Define(
                Guid.NewGuid(), "Agency", 10_240, 10, 5, 4, 2, 3, 8, resellerId: Guid.NewGuid()));
            await seed.SaveChangesAsync();
        }

        await using (var startUp = AccountsTestContext.CreateOn(database, FakeCurrentUser.Anonymous()))
        {
            await new PlanSeeder(startUp).SeedAsync(CancellationToken.None);
        }

        await using var reader = AccountsTestContext.CreateOn(database, FakeCurrentUser.Admin());
        Assert.Equal("Agency", Assert.Single(await reader.Plans.ToListAsync()).Name);
    }
}
