using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Seeders;
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
        return new AccountsDbContext(options);
    }
}
