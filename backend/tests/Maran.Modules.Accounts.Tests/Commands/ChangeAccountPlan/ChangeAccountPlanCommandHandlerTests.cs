using Maran.Modules.Accounts.Commands.ChangeAccountPlan;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.ChangeAccountPlan;

/// <summary>
/// Behavioral contract of <see cref="ChangeAccountPlanCommandHandler"/>: an account moves only to a
/// plan whose disk quota it fits under, the new quota reaches the host, and every outcome is
/// journalled. "Nothing changed" is asserted on the agent's recorded calls and the stored row.
/// </summary>
public sealed class ChangeAccountPlanCommandHandlerTests : IDisposable
{
    /// <summary>One mebibyte in bytes, the unit a quota is pushed to the agent in.</summary>
    private const ulong Mebibyte = 1024UL * 1024UL;

    /// <summary>The module's context, in memory.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Everything the handler journals.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>The agent double that owns the account's disk quota and usage.</summary>
    private readonly RecordingAgentAccountsClient _agent = new();

    /// <summary>The display names of the plans the panel ships.</summary>
    private readonly StubDisplayNames _displayNames = new();

    /// <summary>The plan the account starts on: 10 GB.</summary>
    private readonly Plan _large = Plan.Define(Guid.NewGuid(), "Large", 10_240, 10, 5, 4, 2, 3, 8);

    /// <summary>A smaller plan: 5 GB.</summary>
    private readonly Plan _small = Plan.Define(Guid.NewGuid(), "Small", 5_120, 2, 1, 1, 0, 0, 4);

    /// <summary>A plan selling the same disk as <see cref="_large"/>, with other counts.</summary>
    private readonly Plan _sameDisk = Plan.Define(Guid.NewGuid(), "SameDisk", 10_240, 20, 10, 8, 4, 6, 16);

    /// <summary>The account every test moves.</summary>
    private readonly Account _account;

    /// <summary>Stores the plans and the account.</summary>
    public ChangeAccountPlanCommandHandlerTests()
    {
        _dbContext = AccountsTestContext.Create();
        _dbContext.Plans.AddRange(_large, _small, _sameDisk);
        _account = new Account(
            Guid.NewGuid(),
            "olive",
            "olive.example.com",
            _large.Id,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "owner@example.com");
        _dbContext.Accounts.Add(_account);
        _dbContext.SaveChanges();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _dbContext.Dispose();
    }

    /// <summary>Builds the handler over this test's context, agent and journal.</summary>
    /// <param name="agent">The agent double; this test's own unless a test needs a failing one.</param>
    private ChangeAccountPlanCommandHandler NewHandler(RecordingAgentAccountsClient? agent = null)
    {
        return new ChangeAccountPlanCommandHandler(
            _dbContext,
            new AccountDiskQuotaEnforcer(agent ?? _agent),
            new AccountAuditJournal(_audit, FakeCurrentUser.Admin()),
            _displayNames);
    }

    /// <summary>Builds a move of the test account to <paramref name="planId"/>.</summary>
    /// <param name="planId">The plan to move to.</param>
    /// <param name="accountId">The account to move; the test account unless a test aims elsewhere.</param>
    private ChangeAccountPlanCommand MoveTo(Guid planId, Guid? accountId = null)
    {
        return new ChangeAccountPlanCommand(planId, accountId ?? _account.Id, "203.0.113.7", "unit-tests");
    }

    /// <summary>Re-reads the account's plan from the store.</summary>
    private async Task<Guid> StoredPlanIdAsync()
    {
        return (await _dbContext.Accounts.AsNoTracking().SingleAsync(a => a.Id == _account.Id)).PlanId;
    }

    /// <summary>A move to a smaller plan the account fits under stores the plan and pushes the new quota.</summary>
    [Fact]
    public async Task A_move_to_a_smaller_plan_the_account_fits_under_stores_the_plan_and_pushes_the_new_quota()
    {
        _agent.UsedBytes["olive"] = 5_120 * Mebibyte;

        var result = await NewHandler().HandleAsync(MoveTo(_small.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(_small.Id, await StoredPlanIdAsync());
        Assert.Equal("Small", result.Value.PlanDisplayName);
        Assert.Equal("owner@example.com", result.Value.OwnerEmail);
        Assert.Contains($"quota:olive:{5_120 * Mebibyte}", _agent.Calls);
    }

    /// <summary>A move to a plan whose quota is below the account's usage is refused and changes nothing.</summary>
    [Fact]
    public async Task A_move_to_a_plan_whose_quota_is_below_the_accounts_usage_is_refused_and_changes_nothing()
    {
        _agent.UsedBytes["olive"] = (5_120 * Mebibyte) + 1;

        var result = await NewHandler().HandleAsync(MoveTo(_small.Id), CancellationToken.None);

        Assert.Equal("AccountPlanQuotaBelowUsage", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(_large.Id, await StoredPlanIdAsync());
        Assert.DoesNotContain(_agent.Calls, call =>
        {
            return call.StartsWith("quota:", StringComparison.Ordinal);
        });
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(AuditActions.AccountPlanChanged, entry.Action);
        Assert.Equal("olive: Large -> Small", entry.Subject);
        Assert.False(entry.Succeeded);
    }

    /// <summary>A usage the agent cannot read is refused with the agent's own failure, not assumed to fit.</summary>
    [Fact]
    public async Task A_usage_the_agent_cannot_read_is_refused_with_the_agents_own_failure_not_assumed_to_fit()
    {
        var unavailable = new RecordingAgentAccountsClient(Error.Of("AgentUnavailable", ErrorType.Unavailable));

        var result = await NewHandler(unavailable).HandleAsync(MoveTo(_small.Id), CancellationToken.None);

        Assert.Equal("AgentUnavailable", result.Error!.Code);
        Assert.Equal(_large.Id, await StoredPlanIdAsync());
    }

    /// <summary>A move to a plan selling the same disk touches no quota and is journalled as a success.</summary>
    [Fact]
    public async Task A_move_to_a_plan_selling_the_same_disk_touches_no_quota_and_is_journalled_as_a_success()
    {
        var result = await NewHandler().HandleAsync(MoveTo(_sameDisk.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(_sameDisk.Id, await StoredPlanIdAsync());
        Assert.Empty(_agent.Calls);
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal("olive: Large -> SameDisk", entry.Subject);
        Assert.True(entry.Succeeded);
        Assert.Equal("203.0.113.7", entry.IpAddress);
    }

    /// <summary>A move to a larger plan pushes the larger quota without measuring usage.</summary>
    [Fact]
    public async Task A_move_to_a_larger_plan_pushes_the_larger_quota_without_measuring_usage()
    {
        _dbContext.Accounts.Single(a => a.Id == _account.Id).ChangePlan(_small.Id);
        await _dbContext.SaveChangesAsync();

        var result = await NewHandler().HandleAsync(MoveTo(_large.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([$"quota:olive:{10_240 * Mebibyte}"], _agent.Calls);
    }

    /// <summary>A move to the plan the account is already on changes nothing and journals nothing.</summary>
    [Fact]
    public async Task A_move_to_the_plan_the_account_is_already_on_changes_nothing_and_journals_nothing()
    {
        var result = await NewHandler().HandleAsync(MoveTo(_large.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(_agent.Calls);
        Assert.Empty(_audit.Entries);
    }

    /// <summary>A move to a plan that does not exist is refused as not found and changes nothing.</summary>
    [Fact]
    public async Task A_move_to_a_plan_that_does_not_exist_is_refused_as_not_found_and_changes_nothing()
    {
        var result = await NewHandler().HandleAsync(MoveTo(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal("PlanNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal(_large.Id, await StoredPlanIdAsync());
        Assert.False(Assert.Single(_audit.Entries).Succeeded);
    }

    /// <summary>A move of an account that does not exist is refused as not found and journalled with the id probed.</summary>
    [Fact]
    public async Task A_move_of_an_account_that_does_not_exist_is_refused_as_not_found_and_journalled_with_the_id_probed()
    {
        var missing = Guid.NewGuid();

        var result = await NewHandler().HandleAsync(MoveTo(_small.Id, missing), CancellationToken.None);

        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal(missing.ToString("D"), Assert.Single(_audit.Entries).Subject);
        Assert.Empty(_agent.Calls);
    }
}
