using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Seeders;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Seeders;

/// <summary>
/// Behavioral contract of <see cref="PlanSeeder"/>, run against a real <see cref="AccountsDbContext"/>
/// backed by the EF Core InMemory provider, each test getting its own uniquely-named database
/// (rules/testing.md "Determinism").
/// </summary>
public sealed class PlanSeederTests
{
    /// <summary>Builds a fresh, isolated in-memory <see cref="AccountsDbContext"/>.</summary>
    private static AccountsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AccountsDbContext(options);
    }

    [Fact]
    public async Task Seeding_an_empty_store_inserts_the_three_standard_plans()
    {
        await using var dbContext = CreateDbContext();
        var seeder = new PlanSeeder(dbContext);

        await seeder.SeedAsync(CancellationToken.None);

        var planIds = await dbContext.Plans.Select(p => p.Id).ToListAsync();
        Assert.Equal(3, planIds.Count);
        Assert.Contains(PlanSeeder.StarterPlanId, planIds);
        Assert.Contains(PlanSeeder.BusinessPlanId, planIds);
        Assert.Contains(PlanSeeder.UnlimitedPlanId, planIds);
    }

    [Fact]
    public async Task Seeding_twice_does_not_duplicate_rows()
    {
        await using var dbContext = CreateDbContext();
        var seeder = new PlanSeeder(dbContext);

        await seeder.SeedAsync(CancellationToken.None);
        await seeder.SeedAsync(CancellationToken.None);

        var count = await dbContext.Plans.CountAsync();
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task Seeding_when_one_standard_plan_already_exists_only_inserts_the_missing_ones()
    {
        await using var dbContext = CreateDbContext();
        var seeder = new PlanSeeder(dbContext);
        await seeder.SeedAsync(CancellationToken.None);
        dbContext.Plans.RemoveRange(dbContext.Plans.Where(p => p.Id != PlanSeeder.StarterPlanId));
        await dbContext.SaveChangesAsync();

        await seeder.SeedAsync(CancellationToken.None);

        var planIds = await dbContext.Plans.Select(p => p.Id).ToListAsync();
        Assert.Equal(3, planIds.Count);
        Assert.Contains(PlanSeeder.StarterPlanId, planIds);
        Assert.Contains(PlanSeeder.BusinessPlanId, planIds);
        Assert.Contains(PlanSeeder.UnlimitedPlanId, planIds);
    }

    [Fact]
    public async Task Seeded_plans_carry_ascending_limits_from_starter_to_unlimited()
    {
        await using var dbContext = CreateDbContext();
        var seeder = new PlanSeeder(dbContext);

        await seeder.SeedAsync(CancellationToken.None);

        var starter = await dbContext.Plans.SingleAsync(p => p.Id == PlanSeeder.StarterPlanId);
        var business = await dbContext.Plans.SingleAsync(p => p.Id == PlanSeeder.BusinessPlanId);
        var unlimited = await dbContext.Plans.SingleAsync(p => p.Id == PlanSeeder.UnlimitedPlanId);

        Assert.True(starter.DiskQuotaMb < business.DiskQuotaMb);
        Assert.True(business.DiskQuotaMb < unlimited.DiskQuotaMb);
        Assert.True(starter.MaxSites < business.MaxSites);
        Assert.True(business.MaxSites < unlimited.MaxSites);
    }
}
