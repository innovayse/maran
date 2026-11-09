using Maran.Modules.Accounts.Commands.ChangeAccountPlan;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Tests.TestSupport;

namespace Maran.Modules.Accounts.Tests.Commands.ChangeAccountPlan;

/// <summary>
/// What a plan change pushes to the host for an account that states ceilings of its own (issue #194):
/// <c>override ?? plan</c>, never the plan's figure alone, because the agent keeps no record of the
/// inode and process ceilings and a push without them removes them.
/// </summary>
public sealed class ChangeAccountPlanOverridesTests : IDisposable
{
    /// <summary>The module's context, in memory.</summary>
    private readonly AccountsDbContext _dbContext = AccountsTestContext.Create();

    /// <summary>The agent double.</summary>
    private readonly RecordingAgentAccountsClient _agent = new();

    /// <summary>The plan the account starts on: 10 GB, 64 processes.</summary>
    private readonly Plan _large = Plan.Define(Guid.NewGuid(), "Large", 10_240, 10, 5, 4, 2, 3, 8);

    /// <summary>A smaller plan: 5 GB, 128 processes.</summary>
    private readonly Plan _small = Plan.Define(Guid.NewGuid(), "Small", 5_120, 2, 1, 1, 0, 0, 4);

    /// <summary>Stores the plans.</summary>
    public ChangeAccountPlanOverridesTests()
    {
        _large.ChangeRuntimeLimits(Processes(64));
        _small.ChangeRuntimeLimits(Processes(128));
        _dbContext.Plans.AddRange(_large, _small);
        _dbContext.SaveChanges();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _dbContext.Dispose();
    }

    /// <summary>States runtime ceilings that differ only in the process figure.</summary>
    /// <param name="processes">The process ceiling.</param>
    /// <returns>The ceilings.</returns>
    private static RuntimeLimits Processes(int processes)
    {
        return RuntimeLimits.Of(ResourceLimit.Unlimited(), ResourceLimit.Unlimited(), ResourceLimit.Limited(processes));
    }

    /// <summary>Stores an account on the large plan holding the given overrides, and moves it to the small one.</summary>
    /// <param name="overrides">What the account states in place of its plan's.</param>
    private async Task MoveToSmallAsync(AccountLimitOverrides overrides)
    {
        _dbContext.Accounts.Add(new Account(
            Guid.NewGuid(), "olive", "olive.example.com", _large.Id, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "owner@example.com", limitOverrides: overrides));
        await _dbContext.SaveChangesAsync();
        var account = _dbContext.Accounts.Single();

        var result = await AccountHandlerFactory
            .ChangePlan(_dbContext, FakeCurrentUser.Admin(), _agent, new RecordingAuditWriter())
            .HandleAsync(new ChangeAccountPlanCommand(_small.Id, account.Id, "203.0.113.7", "unit-tests"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Code);
    }

    /// <summary>The quota pushed after a plan change carries the account's own inode ceiling.</summary>
    [Fact]
    public async Task The_quota_pushed_after_a_plan_change_carries_the_accounts_own_inode_ceiling()
    {
        await MoveToSmallAsync(AccountLimitOverrides.Of(50_000, null, null));

        Assert.Equal(("olive", (ulong?)50_000UL), Assert.Single(_agent.QuotaInodeLimits));
    }

    /// <summary>An account that follows its plan has its quota pushed with no inode ceiling: the control.</summary>
    [Fact]
    public async Task An_account_that_follows_its_plan_has_its_quota_pushed_with_no_inode_ceiling()
    {
        await MoveToSmallAsync(AccountLimitOverrides.None());

        Assert.Equal(("olive", (ulong?)null), Assert.Single(_agent.QuotaInodeLimits));
    }

    /// <summary>An account that follows its plan has the new plan's process ceiling pushed.</summary>
    [Fact]
    public async Task An_account_that_follows_its_plan_has_the_new_plans_process_ceiling_pushed()
    {
        await MoveToSmallAsync(AccountLimitOverrides.None());

        Assert.Contains("limits:olive:Unlimited0/Unlimited0/Limited128", _agent.Calls);
    }

    /// <summary>An account stating its own process ceiling is pushed nothing for a plan difference it does not see.</summary>
    [Fact]
    public async Task An_account_stating_its_own_process_ceiling_is_pushed_nothing_for_a_plan_difference_it_does_not_see()
    {
        await MoveToSmallAsync(AccountLimitOverrides.Of(null, 10, null));

        Assert.DoesNotContain(_agent.Calls, call => { return call.StartsWith("limits:", StringComparison.Ordinal); });
    }
}
