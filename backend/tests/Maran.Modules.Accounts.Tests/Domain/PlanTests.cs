using Maran.Modules.Accounts.Domain;

namespace Maran.Modules.Accounts.Tests.Domain;

/// <summary>Behavioral contract of the <see cref="Plan"/> entity.</summary>
public sealed class PlanTests
{
    /// <summary>A plan with a non positive worker budget cannot be created.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_plan_with_a_non_positive_worker_budget_cannot_be_created(int workers)
    {
        // The budget becomes pm.max_children, and php-fpm refuses to start a pool with a
        // non-positive one — so a plan carrying zero is a plan whose sites cannot serve PHP. The
        // boundary refuses it here rather than letting it reach a rendered config, where the only
        // symptom is a pool that will not start.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            return new Plan(Guid.NewGuid(), "PlanStarterName", 5_120, 5, 2, 3, workers);
        });
    }

    /// <summary>A plan with a positive worker budget is created.</summary>
    [Fact]
    public void A_plan_with_a_positive_worker_budget_is_created()
    {
        var plan = new Plan(Guid.NewGuid(), "PlanStarterName", 5_120, 5, 2, 3, 7);

        Assert.Equal(7, plan.MaxPhpWorkersPerPool);
    }
}
