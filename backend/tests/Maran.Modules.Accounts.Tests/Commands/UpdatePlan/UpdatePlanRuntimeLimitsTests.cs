using Maran.Modules.Accounts.Commands.UpdatePlan;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.UpdatePlan;

/// <summary>
/// How a plan edit reaches its accounts' runtime ceilings (issue #133): pushed to every account on the
/// plan when they change, never when they do not, and the row left alone when the agent refuses.
/// </summary>
public sealed class UpdatePlanRuntimeLimitsTests : IDisposable
{
    /// <summary>The module's context, in memory.</summary>
    private readonly AccountsDbContext _dbContext = AccountsTestContext.Create();

    /// <summary>The agent double; records every call.</summary>
    private readonly RecordingAgentAccountsClient _agent = new();

    /// <summary>The display names of the plans the panel ships.</summary>
    private readonly StubDisplayNames _displayNames = new();

    /// <summary>The plan every test edits, with no runtime ceilings yet.</summary>
    private readonly Plan _plan = Plan.Define(Guid.NewGuid(), "Agency", 10_240, 10, 5, 4, 2, 3, 8);

    /// <summary>Stores the plan and two accounts on it.</summary>
    public UpdatePlanRuntimeLimitsTests()
    {
        _dbContext.Plans.Add(_plan);
        foreach (var name in new[] { "bravo", "alpha" })
        {
            _dbContext.Accounts.Add(new Account(
                Guid.NewGuid(), name, $"{name}.example.com", _plan.Id, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        }

        _dbContext.SaveChanges();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _dbContext.Dispose();
    }

    /// <summary>Changed ceilings are pushed to every account on the plan, in name order, and then stored.</summary>
    [Fact]
    public async Task Changed_ceilings_are_pushed_to_every_account_on_the_plan_and_then_stored()
    {
        var result = await NewHandler().HandleAsync(
            Edit(ResourceCeilingTestValues.Limited(200), ResourceCeilingTestValues.Limited(2_048)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ["limits:alpha:Limited200/Limited2048/Unlimited0", "limits:bravo:Limited200/Limited2048/Unlimited0"],
            _agent.Calls);
        var stored = await _dbContext.Plans.AsNoTracking().SingleAsync();
        Assert.Equal(ResourceLimitKind.Limited, stored.RuntimeLimits.CpuQuotaPercent.Kind);
        Assert.Equal(2_048, stored.RuntimeLimits.MemoryMaxMb.Maximum);
        Assert.Equal(ResourceLimitKind.Unlimited, stored.RuntimeLimits.MaxProcesses.Kind);
        Assert.Equal(ResourceLimitKind.Limited, result.Value!.CpuQuotaPercent.Kind);
        Assert.Equal(200, result.Value.CpuQuotaPercent.Maximum);
    }

    /// <summary>Unchanged ceilings are not pushed, so an edit of a name or a count never needs the agent.</summary>
    [Fact]
    public async Task Unchanged_ceilings_are_not_pushed()
    {
        var result = await NewHandler().HandleAsync(
            Edit(ResourceCeilingTestValues.Unlimited, ResourceCeilingTestValues.Unlimited) with { MaxSites = 11 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(_agent.Calls);
    }

    /// <summary>A refused push leaves the plan's ceilings as they were.</summary>
    [Fact]
    public async Task A_refused_push_leaves_the_plans_ceilings_as_they_were()
    {
        _agent.RuntimeLimitRefusals["bravo"] = Error.Of("AgentSystemFailure", ErrorType.Failure);

        var result = await NewHandler().HandleAsync(
            Edit(ResourceCeilingTestValues.Limited(50), ResourceCeilingTestValues.Unlimited),
            CancellationToken.None);

        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        var stored = await _dbContext.Plans.AsNoTracking().SingleAsync();
        Assert.Equal(ResourceLimitKind.Unlimited, stored.RuntimeLimits.CpuQuotaPercent.Kind);
    }

    /// <summary>Builds the handler over this test's context and agent.</summary>
    /// <returns>The handler.</returns>
    private UpdatePlanCommandHandler NewHandler()
    {
        return new UpdatePlanCommandHandler(
            _dbContext,
            new AccountDiskQuotaEnforcer(_agent),
            new AccountRuntimeLimitsEnforcer(_agent),
            new PlanNameDirectory(_dbContext, _displayNames),
            new AccountAuditJournal(new RecordingAuditWriter(), FakeCurrentUser.Admin()),
            _displayNames);
    }

    /// <summary>Builds an edit of the plan that keeps every other limit and states these ceilings.</summary>
    /// <param name="cpu">The CPU ceiling.</param>
    /// <param name="memory">The memory ceiling.</param>
    /// <returns>The command.</returns>
    private UpdatePlanCommand Edit(Common.ResourceCeilingDto cpu, Common.ResourceCeilingDto memory)
    {
        return new UpdatePlanCommand(
            "Agency", 10_240, 10, 5, 4, 2, 3, 8, cpu, memory, ResourceCeilingTestValues.Unlimited, _plan.Id, "203.0.113.7", "unit-tests");
    }
}
