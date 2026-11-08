using Maran.Modules.Accounts.Commands.UpdatePlan;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Extensions;

namespace Maran.Modules.Accounts.Tests.Commands.UpdatePlan;

/// <summary>
/// A plan edit reaches every account on the plan, and an account that states ceilings of its own keeps
/// them on the host (issue #194): the quota carries the account's inode ceiling and the slice keeps
/// its process ceiling, because the agent keeps no record of either.
/// </summary>
public sealed class UpdatePlanAccountOverridesTests : IDisposable
{
    /// <summary>The module's context, in memory.</summary>
    private readonly AccountsDbContext _dbContext = AccountsTestContext.Create();

    /// <summary>The agent double; records every call.</summary>
    private readonly RecordingAgentAccountsClient _agent = new();

    /// <summary>The plan every test edits: 10 GB, no runtime ceilings.</summary>
    private readonly Plan _plan = Plan.Define(Guid.NewGuid(), "Agency", 10_240, 10, 5, 4, 2, 3, 8);

    /// <summary>Stores the plan and two accounts on it: one with its own ceilings, one following the plan.</summary>
    public UpdatePlanAccountOverridesTests()
    {
        _dbContext.Plans.Add(_plan);
        var created = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        _dbContext.Accounts.Add(new Account(
            Guid.NewGuid(), "alpha", "alpha.example.com", _plan.Id, created, limitOverrides: AccountLimitOverrides.Of(50_000, 10, null)));
        _dbContext.Accounts.Add(new Account(Guid.NewGuid(), "bravo", "bravo.example.com", _plan.Id, created));
        _dbContext.SaveChanges();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _dbContext.Dispose();
    }

    /// <summary>Builds an edit of the plan stating this disk quota and this process ceiling.</summary>
    /// <param name="diskQuotaMb">The disk quota.</param>
    /// <param name="processes">The process ceiling.</param>
    /// <returns>The command.</returns>
    private UpdatePlanCommand Edit(int diskQuotaMb, ResourceCeilingDto processes)
    {
        return new UpdatePlanCommand(
            "Agency", diskQuotaMb, 10, 5, 4, 2, 3, 8, ResourceCeilingTestValues.Unlimited, ResourceCeilingTestValues.Unlimited,
            processes, AccountFeature.All.Members(), PlanId: _plan.Id, IpAddress: "203.0.113.7", UserAgent: "unit-tests");
    }

    /// <summary>Builds the handler over this test's context and agent.</summary>
    /// <returns>The handler.</returns>
    private UpdatePlanCommandHandler NewHandler()
    {
        return PlanHandlerFactory.Update(
            _dbContext, FakeCurrentUser.Admin(), new RecordingAuditWriter(), new StubDisplayNames(), _agent);
    }

    /// <summary>A quota edit pushes each account's own inode ceiling beside the new quota.</summary>
    [Fact]
    public async Task A_quota_edit_pushes_each_accounts_own_inode_ceiling_beside_the_new_quota()
    {
        var result = await NewHandler().HandleAsync(
            Edit(20_480, ResourceCeilingTestValues.Unlimited), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal([("alpha", (ulong?)50_000UL), ("bravo", (ulong?)null)], _agent.QuotaInodeLimits);
    }

    /// <summary>A process ceiling edit keeps an account's own process ceiling and gives the other the plan's.</summary>
    [Fact]
    public async Task A_process_ceiling_edit_keeps_an_accounts_own_ceiling_and_gives_the_other_the_plans()
    {
        var result = await NewHandler().HandleAsync(
            Edit(10_240, ResourceCeilingTestValues.Limited(200)), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal(
            ["limits:alpha:Unlimited0/Unlimited0/Limited10", "limits:bravo:Unlimited0/Unlimited0/Limited200"],
            _agent.Calls);
    }
}
