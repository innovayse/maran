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
