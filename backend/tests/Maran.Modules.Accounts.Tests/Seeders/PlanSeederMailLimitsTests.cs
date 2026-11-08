using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Seeders;
using Maran.Modules.Accounts.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Seeders;

/// <summary>The mail allowance the three shipped plans carry (issue #177, plan decision D13).</summary>
public sealed class PlanSeederMailLimitsTests
{
    /// <summary>Each shipped plan carries the allowance of its rung of the ladder.</summary>
    [Theory]
    [InlineData("11111111-0000-4000-8000-000000000001", 5, 1_024, 100, 5_120)]
    [InlineData("11111111-0000-4000-8000-000000000002", 10, 1_024, 100, 10_240)]
    [InlineData("11111111-0000-4000-8000-000000000003", 100, 2_048, 500, 204_800)]
    public async Task Each_shipped_plan_carries_the_allowance_of_its_rung(
        string planId,
        int mailboxes,
        int quota,
        int sendLimit,
        int pool)
    {
        await using var dbContext = AccountsTestContext.Create();
        await new PlanSeeder(dbContext).SeedAsync(CancellationToken.None);

        var plan = await dbContext.Plans.AsNoTracking().SingleAsync(p => p.Id == Guid.Parse(planId));

        Assert.Equal(MailLimits.Of(mailboxes, quota, sendLimit, pool), plan.MailLimits);
    }

    /// <summary>The middle rung is the default, so the ladder starts from what an existing plan reads as.</summary>
    [Fact]
    public async Task The_middle_rung_is_the_default()
    {
        await using var dbContext = AccountsTestContext.Create();
        await new PlanSeeder(dbContext).SeedAsync(CancellationToken.None);

        var plan = await dbContext.Plans.AsNoTracking().SingleAsync(p => p.Id == PlanSeeder.BusinessPlanId);

        Assert.Equal(MailLimits.Default, plan.MailLimits);
    }

    /// <summary>Seeding leaves an operator's own plan with the mail figures it holds.</summary>
    [Fact]
    public async Task Seeding_leaves_an_operators_own_plan_with_its_mail_figures()
    {
        await using var dbContext = AccountsTestContext.Create();
        var own = Plan.Define(Guid.NewGuid(), "Agency", 10_240, 10, 5, 4, 2, 3, 8);
        own.ChangeMailLimits(MailLimits.Of(3, 256, 20, 768));
        dbContext.Plans.Add(own);
        await dbContext.SaveChangesAsync();

        await new PlanSeeder(dbContext).SeedAsync(CancellationToken.None);

        var stored = Assert.Single(await dbContext.Plans.AsNoTracking().ToListAsync());
        Assert.Equal(MailLimits.Of(3, 256, 20, 768), stored.MailLimits);
    }
}
