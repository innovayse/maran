using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Queries.CountCustomPlans;
using Maran.Modules.Accounts.Seeders;
using Maran.Modules.Accounts.Tests.TestSupport;

namespace Maran.Modules.Accounts.Tests.Queries.CountCustomPlans;

/// <summary>Behavioral contract of <see cref="CountCustomPlansQueryHandler"/>.</summary>
public sealed class CountCustomPlansQueryHandlerTests
{
    /// <summary>The three plans a fresh installation seeds are not the administrator's work, so they count for nothing.</summary>
    [Fact]
    public async Task The_three_seeded_plans_alone_count_as_zero()
    {
        await using var dbContext = AccountsTestContext.Create();
        dbContext.Plans.AddRange(
            Plan(PlanSeeder.StarterPlanId),
            Plan(PlanSeeder.BusinessPlanId),
            Plan(PlanSeeder.UnlimitedPlanId));
        await dbContext.SaveChangesAsync();

        var result = await new CountCustomPlansQueryHandler(dbContext)
            .HandleAsync(new CountCustomPlansQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);
    }

    /// <summary>
    /// A plan the administrator made counts, beside the seeded ones, which still do not: the inverse
    /// control that the query is not simply returning zero.
    /// </summary>
    [Fact]
    public async Task A_plan_the_administrator_made_counts_and_the_seeded_ones_beside_it_do_not()
    {
        await using var dbContext = AccountsTestContext.Create();
        dbContext.Plans.AddRange(
            Plan(PlanSeeder.StarterPlanId),
            Plan(PlanSeeder.BusinessPlanId),
            Plan(PlanSeeder.UnlimitedPlanId),
            Plan(Guid.NewGuid()),
            Plan(Guid.NewGuid()));
        await dbContext.SaveChangesAsync();

        var result = await new CountCustomPlansQueryHandler(dbContext)
            .HandleAsync(new CountCustomPlansQuery(), CancellationToken.None);

        Assert.Equal(2, result.Value);
    }

    /// <summary>A plan owned by a reseller is that reseller's work and does not tick the administrator's step.</summary>
    [Fact]
    public async Task A_resellers_plan_does_not_count()
    {
        await using var dbContext = AccountsTestContext.Create();
        dbContext.Plans.Add(Plan(Guid.NewGuid(), resellerId: Guid.NewGuid()));
        await dbContext.SaveChangesAsync();

        var result = await new CountCustomPlansQueryHandler(dbContext)
            .HandleAsync(new CountCustomPlansQuery(), CancellationToken.None);

        Assert.Equal(0, result.Value);
    }

    /// <summary>Builds a plan with the given identity.</summary>
    /// <param name="id">The plan's identity.</param>
    /// <param name="resellerId">The reseller that owns it, or <c>null</c> for the host's own plan.</param>
    /// <returns>The plan.</returns>
    private static Plan Plan(Guid id, Guid? resellerId = null)
    {
        return new Plan(id, "PlanStarterName", 5_120, 5, 2, 3, 5, 5, resellerId: resellerId);
    }
}
