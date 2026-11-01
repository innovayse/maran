using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Domain.ValueObjects;

namespace Maran.Modules.Accounts.Tests.Domain;

/// <summary>The rules <see cref="RuntimeLimits"/> holds itself to (issue #133).</summary>
public sealed class RuntimeLimitsTests
{
    /// <summary>A limited ceiling of zero is refused: it is an account that cannot run, not a smaller one.</summary>
    [Fact]
    public void A_limited_ceiling_of_zero_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            RuntimeLimits.Of(ResourceLimit.Limited(0), ResourceLimit.Unlimited(), ResourceLimit.Unlimited());
        });
    }

    /// <summary>A plan that never stated ceilings is unlimited on all three.</summary>
    [Fact]
    public void A_plan_that_never_stated_ceilings_is_unlimited_on_all_three()
    {
        var plan = Accounts.Domain.Entities.Plan.Define(Guid.NewGuid(), "Agency", 10_240, 10, 5, 4, 2, 3, 8);

        Assert.Equal(ResourceLimitKind.Unlimited, plan.RuntimeLimits.CpuQuotaPercent.Kind);
        Assert.Equal(ResourceLimitKind.Unlimited, plan.RuntimeLimits.MemoryMaxMb.Kind);
        Assert.Equal(ResourceLimitKind.Unlimited, plan.RuntimeLimits.MaxProcesses.Kind);
    }

    /// <summary>One ceiling instance passed for all three still yields three values the store can own apart.</summary>
    [Fact]
    public void One_instance_passed_for_all_three_yields_three_separate_values()
    {
        var shared = ResourceLimit.Limited(10);

        var limits = RuntimeLimits.Of(shared, shared, shared);

        Assert.NotSame(limits.CpuQuotaPercent, limits.MemoryMaxMb);
        Assert.NotSame(limits.MemoryMaxMb, limits.MaxProcesses);
        Assert.Equal(limits.CpuQuotaPercent, limits.MaxProcesses);
    }
}
