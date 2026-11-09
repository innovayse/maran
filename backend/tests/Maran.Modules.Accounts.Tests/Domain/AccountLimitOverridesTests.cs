using Maran.Modules.Accounts.Domain.ValueObjects;

namespace Maran.Modules.Accounts.Tests.Domain;

/// <summary>
/// The ceilings an account states for itself: a stated figure is positive, an unstated one follows the
/// plan, and the process figure is the only one that meets the plan's runtime ceilings.
/// </summary>
public sealed class AccountLimitOverridesTests
{
    /// <summary>The plan ceilings every test applies the overrides to: CPU 150, no memory ceiling, 64 processes.</summary>
    private static readonly RuntimeLimits PlanCeilings = RuntimeLimits.Of(
        ResourceLimit.Limited(150), ResourceLimit.Unlimited(), ResourceLimit.Limited(64));

    /// <summary>No override is empty and leaves the plan's ceilings exactly as they are.</summary>
    [Fact]
    public void No_override_is_empty_and_leaves_the_plans_ceilings_as_they_are()
    {
        var none = AccountLimitOverrides.None();

        Assert.True(none.IsEmpty);
        Assert.Equal(PlanCeilings, none.ApplyTo(PlanCeilings));
    }

    /// <summary>A stated process figure replaces the plan's and leaves its CPU and memory alone.</summary>
    [Fact]
    public void A_stated_process_figure_replaces_the_plans_and_leaves_cpu_and_memory_alone()
    {
        var applied = AccountLimitOverrides.Of(null, 10, null).ApplyTo(PlanCeilings);

        Assert.Equal(10, applied.MaxProcesses.Maximum);
        Assert.Equal(150, applied.CpuQuotaPercent.Maximum);
        Assert.Null(applied.MemoryMaxMb.Maximum);
    }

    /// <summary>A stated process figure also stands where the plan states no process ceiling.</summary>
    [Fact]
    public void A_stated_process_figure_also_stands_where_the_plan_states_none()
    {
        var applied = AccountLimitOverrides.Of(null, 10, null).ApplyTo(RuntimeLimits.Unlimited());

        Assert.Equal(10, applied.MaxProcesses.Maximum);
    }

    /// <summary>Inode and open-file figures are carried as stated and do not touch the runtime ceilings.</summary>
    [Fact]
    public void Inode_and_open_file_figures_are_carried_and_do_not_touch_the_runtime_ceilings()
    {
        var overrides = AccountLimitOverrides.Of(50_000, null, 2_048);

        Assert.False(overrides.IsEmpty);
        Assert.Equal(50_000, overrides.InodeLimit);
        Assert.Equal(2_048, overrides.OpenFilesLimit);
        Assert.Equal(PlanCeilings, overrides.ApplyTo(PlanCeilings));
    }

    /// <summary>A stated figure that is not positive is refused, for each of the three.</summary>
    [Fact]
    public void A_stated_figure_that_is_not_positive_is_refused_for_each_of_the_three()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => { return AccountLimitOverrides.Of(0, null, null); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { return AccountLimitOverrides.Of(null, 0, null); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { return AccountLimitOverrides.Of(null, null, -1); });
    }

    /// <summary>Two empty overrides are two instances, so no two accounts share one owned value.</summary>
    [Fact]
    public void Two_empty_overrides_are_two_instances()
    {
        Assert.NotSame(AccountLimitOverrides.None(), AccountLimitOverrides.None());
    }
}
