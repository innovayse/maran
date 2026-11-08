using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Seeders;
using Maran.Modules.Accounts.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Seeders;

/// <summary>The PHP bounds the three shipped plans carry (issue #138, owner decision O2).</summary>
public sealed class PlanSeederPhpLimitsTests
{
    /// <summary>Each shipped plan carries the bounds of its rung of the ladder.</summary>
    [Theory]
    [InlineData("11111111-0000-4000-8000-000000000001", 128, 32, 60, 1_000)]
    [InlineData("11111111-0000-4000-8000-000000000002", 256, 64, 120, 3_000)]
    [InlineData("11111111-0000-4000-8000-000000000003", 512, 128, 300, 10_000)]
    public async Task Each_shipped_plan_carries_the_bounds_of_its_rung(
        string planId,
        int memory,
        int upload,
        int seconds,
        int variables)
    {
        await using var dbContext = AccountsTestContext.Create();
        await new PlanSeeder(dbContext).SeedAsync(CancellationToken.None);

        var plan = await dbContext.Plans.AsNoTracking().SingleAsync(p => p.Id == Guid.Parse(planId));

        Assert.Equal(PhpLimits.Of(memory, upload, seconds, variables), plan.PhpLimits);
    }

    /// <summary>The middle rung is the default, so the ladder starts from what an existing plan reads as.</summary>
    [Fact]
    public async Task The_middle_rung_is_the_default()
    {
        await using var dbContext = AccountsTestContext.Create();
        await new PlanSeeder(dbContext).SeedAsync(CancellationToken.None);

        var plan = await dbContext.Plans.AsNoTracking().SingleAsync(p => p.Id == PlanSeeder.BusinessPlanId);

        Assert.Equal(PhpLimits.Default, plan.PhpLimits);
    }

    /// <summary>Seeding again does not overwrite the bounds an operator edited.</summary>
    [Fact]
    public async Task Seeding_again_does_not_overwrite_the_bounds_an_operator_edited()
    {
        await using var dbContext = AccountsTestContext.Create();
        var seeder = new PlanSeeder(dbContext);
        await seeder.SeedAsync(CancellationToken.None);
        var starter = await dbContext.Plans.SingleAsync(p => p.Id == PlanSeeder.StarterPlanId);
        starter.ChangePhpLimits(PhpLimits.Of(300, 100, 200, 5_000));
        await dbContext.SaveChangesAsync();

        await seeder.SeedAsync(CancellationToken.None);

        var stored = await dbContext.Plans.AsNoTracking().SingleAsync(p => p.Id == PlanSeeder.StarterPlanId);
        Assert.Equal(PhpLimits.Of(300, 100, 200, 5_000), stored.PhpLimits);
    }

    /// <summary>Seeding adds nothing to an operator's plan.</summary>
    [Fact]
    public async Task Seeding_leaves_an_operators_own_plan_with_its_bounds()
    {
        await using var dbContext = AccountsTestContext.Create();
        var own = Plan.Define(Guid.NewGuid(), "Agency", 10_240, 10, 5, 4, 2, 3, 8);
        own.ChangePhpLimits(PhpLimits.Of(64, 8, 30, 500));
        dbContext.Plans.Add(own);
        await dbContext.SaveChangesAsync();

        await new PlanSeeder(dbContext).SeedAsync(CancellationToken.None);

        var stored = Assert.Single(await dbContext.Plans.AsNoTracking().ToListAsync());
        Assert.Equal(PhpLimits.Of(64, 8, 30, 500), stored.PhpLimits);
    }
}
