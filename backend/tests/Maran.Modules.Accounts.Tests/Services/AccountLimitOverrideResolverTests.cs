using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Accounts.Tests.Services;

/// <summary>
/// Which ceilings a new account holds in place of its plan's: an administrator may state any figure, a
/// reseller may only lower the plan's, and the wire's zero is "no limit", stored as not stated.
/// </summary>
public sealed class AccountLimitOverrideResolverTests : IDisposable
{
    /// <summary>The module's context, in memory; the resolver reads no row from it.</summary>
    private readonly AccountsDbContext _dbContext = AccountsTestContext.Create();

    /// <summary>A plan holding 64 processes.</summary>
    private readonly Plan _limited = LimitedPlan(64);

    /// <summary>A plan stating no process ceiling.</summary>
    private readonly Plan _unlimited = Plan.Define(Guid.NewGuid(), "Free", 1_000, 5, 5, 5, 5, 5, 5);

    /// <inheritdoc />
    public void Dispose()
    {
        _dbContext.Dispose();
    }

    /// <summary>Builds a plan whose process ceiling is <paramref name="processes"/>.</summary>
    /// <param name="processes">The ceiling.</param>
    /// <returns>The plan.</returns>
    private static Plan LimitedPlan(int processes)
    {
        var plan = Plan.Define(Guid.NewGuid(), "Limited", 1_000, 5, 5, 5, 5, 5, 5);
        plan.ChangeRuntimeLimits(RuntimeLimits.Of(
            ResourceLimit.Unlimited(), ResourceLimit.Unlimited(), ResourceLimit.Limited(processes)));
        return plan;
    }

    /// <summary>Builds the resolver for a caller.</summary>
    /// <param name="caller">The caller creating the account.</param>
    /// <returns>The resolver.</returns>
    private AccountLimitOverrideResolver ResolverFor(FakeCurrentUser caller)
    {
        return new AccountLimitOverrideResolver(new ResellerScope(_dbContext, caller));
    }

    /// <summary>An administrator may state a process ceiling above the plan's.</summary>
    [Fact]
    public void An_administrator_may_state_a_process_ceiling_above_the_plans()
    {
        var result = ResolverFor(FakeCurrentUser.Admin()).Resolve(null, 500, null, _limited);

        Assert.True(result.IsSuccess);
        Assert.Equal(500, result.Value.ProcessLimit);
    }

    /// <summary>A reseller may not state a process ceiling above the plan's.</summary>
    [Fact]
    public void A_reseller_may_not_state_a_process_ceiling_above_the_plans()
    {
        var result = ResolverFor(FakeCurrentUser.Reseller(Guid.NewGuid())).Resolve(null, 65, null, _limited);

        Assert.Equal("AccountLimitAbovePlan", result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    /// <summary>A reseller may state the plan's own figure and any lower one: only a higher one is refused.</summary>
    /// <param name="processes">The figure stated.</param>
    [Theory]
    [InlineData(64)]
    [InlineData(1)]
    public void A_reseller_may_state_the_plans_own_figure_and_any_lower_one(int processes)
    {
        var result = ResolverFor(FakeCurrentUser.Reseller(Guid.NewGuid())).Resolve(null, processes, null, _limited);

        Assert.True(result.IsSuccess);
        Assert.Equal(processes, result.Value.ProcessLimit);
    }

    /// <summary>A reseller's key is held to the plan the same as the reseller's login.</summary>
    [Fact]
    public void A_resellers_api_key_is_held_to_the_plan_the_same_as_its_login()
    {
        var result = ResolverFor(FakeCurrentUser.ApiKey(Guid.NewGuid())).Resolve(null, 65, null, _limited);

        Assert.Equal("AccountLimitAbovePlan", result.Error!.Code);
    }

    /// <summary>A plan that states no process ceiling leaves a reseller free to state any figure: every one is lower.</summary>
    [Fact]
    public void A_plan_that_states_no_process_ceiling_leaves_a_reseller_free_to_state_any_figure()
    {
        var result = ResolverFor(FakeCurrentUser.Reseller(Guid.NewGuid())).Resolve(null, 4_000_000, null, _unlimited);

        Assert.True(result.IsSuccess);
        Assert.Equal(4_000_000, result.Value.ProcessLimit);
    }

    /// <summary>The wire's zero for inodes and open files is stored as not stated, for an administrator and a reseller alike.</summary>
    [Fact]
    public void The_wires_zero_for_inodes_and_open_files_is_stored_as_not_stated()
    {
        var admin = ResolverFor(FakeCurrentUser.Admin()).Resolve(0, null, 0, _limited);
        var reseller = ResolverFor(FakeCurrentUser.Reseller(Guid.NewGuid())).Resolve(0, null, 0, _limited);

        Assert.True(admin.Value.IsEmpty);
        Assert.True(reseller.Value.IsEmpty);
    }

    /// <summary>Stated inode and open-file figures are kept, since no plan states a figure for a reseller to exceed.</summary>
    [Fact]
    public void Stated_inode_and_open_file_figures_are_kept_for_a_reseller()
    {
        var result = ResolverFor(FakeCurrentUser.Reseller(Guid.NewGuid())).Resolve(25_000, null, 4_096, _limited);

        Assert.Equal(25_000, result.Value.InodeLimit);
        Assert.Equal(4_096, result.Value.OpenFilesLimit);
    }

    /// <summary>Stating nothing follows the plan.</summary>
    [Fact]
    public void Stating_nothing_follows_the_plan()
    {
        var result = ResolverFor(FakeCurrentUser.Admin()).Resolve(null, null, null, _limited);

        Assert.True(result.Value.IsEmpty);
    }
}
