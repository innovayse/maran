using Maran.Modules.Accounts.Commands.UpdatePlan;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.UpdatePlan;

/// <summary>
/// Behavioral contract of <see cref="UpdatePlanCommandHandler"/>, above all the rule it chose for a
/// shrinking disk quota: an edit that would put an account over its limit is REFUSED WHOLE, and
/// nothing — no row, no kernel quota — is changed. The agent double records every call, so "nothing
/// was changed" is asserted on the calls and the stored row, not inferred from the result.
/// </summary>
public sealed class UpdatePlanCommandHandlerTests : IDisposable
{
    /// <summary>One mebibyte in bytes, the unit a quota is pushed to the agent in.</summary>
    private const ulong Mebibyte = 1024UL * 1024UL;

    /// <summary>The module's context, in memory.</summary>
    private readonly AccountsDbContext _dbContext = AccountsTestContext.Create();

    /// <summary>Everything the handler journals.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>The agent double that owns the accounts' disk quotas.</summary>
    private readonly RecordingAgentAccountsClient _agent = new();

    /// <summary>The display names of the plans the panel ships.</summary>
    private readonly StubDisplayNames _displayNames = new StubDisplayNames().With("PlanStarterName", "Starter");

    /// <summary>The plan every test edits: an operator's own, 10 GB, 10 sites.</summary>
    private readonly Plan _plan = Plan.Define(Guid.NewGuid(), "Agency", 10_240, 10, 5, 4, 2, 3, 8);

    /// <summary>Stores the plan the tests edit.</summary>
    public UpdatePlanCommandHandlerTests()
    {
        _dbContext.Plans.Add(_plan);
        _dbContext.SaveChanges();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _dbContext.Dispose();
    }

    /// <summary>Builds the handler over this test's context, agent and journal.</summary>
    private UpdatePlanCommandHandler NewHandler()
    {
        return new UpdatePlanCommandHandler(
            _dbContext,
            _agent,
            new PlanNameDirectory(_dbContext, _displayNames),
            new AccountAuditJournal(_audit, FakeCurrentUser.Admin()),
            _displayNames);
    }

    /// <summary>Builds an edit of <see cref="_plan"/> that changes nothing, for a test to vary.</summary>
    /// <param name="planId">The plan to edit; <see cref="_plan"/> unless a test aims elsewhere.</param>
    private UpdatePlanCommand Same(Guid? planId = null)
    {
        return new("Agency", 10_240, 10, 5, 4, 2, 3, 8, planId ?? _plan.Id, "203.0.113.7", "unit-tests");
    }

    /// <summary>Puts an account on <see cref="_plan"/> and returns its name.</summary>
    /// <param name="name">The account's name.</param>
    /// <param name="plan">The plan to put it on; <see cref="_plan"/> unless a test says otherwise.</param>
    private async Task AddAccountAsync(string name, Plan? plan = null)
    {
        _dbContext.Accounts.Add(new Account(
            Guid.NewGuid(), name, $"{name}.example.com", (plan ?? _plan).Id, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        await _dbContext.SaveChangesAsync();
    }

    /// <summary>Re-reads the plan from the store, bypassing what the context already tracks.</summary>
    private async Task<Plan> ReloadAsync(Guid? planId = null)
    {
        return await _dbContext.Plans.AsNoTracking().SingleAsync(plan => plan.Id == (planId ?? _plan.Id));
    }

    /// <summary>An edit of a plan that does not exist is refused as not found and journalled with the id probed.</summary>
    [Fact]
    public async Task An_edit_of_a_plan_that_does_not_exist_is_refused_as_not_found_and_journalled_with_the_id_probed()
    {
        var missing = Guid.NewGuid();

        var result = await NewHandler().HandleAsync(Same(missing), CancellationToken.None);

        Assert.Equal("PlanNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(AuditActions.PlanUpdated, entry.Action);
        Assert.Equal(missing.ToString("D"), entry.Subject);
        Assert.False(entry.Succeeded);
        Assert.Empty(_agent.Calls);
    }

    /// <summary>An edit stores every limit it states and returns the plan with its account count.</summary>
    [Fact]
    public async Task An_edit_stores_every_limit_it_states_and_returns_the_plan_with_its_account_count()
    {
        await AddAccountAsync("olive");
        await AddAccountAsync("pepper");

        var result = await NewHandler().HandleAsync(
            Same() with { MaxSites = 11, MaxDatabases = 6, MaxSftpUsers = 7, MaxFtpUsers = 9, MaxCronEntries = 12, MaxPhpWorkersPerPool = 13 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var stored = await ReloadAsync();
        Assert.Equal(11, stored.MaxSites);
        Assert.Equal(6, stored.MaxDatabases);
        Assert.Equal(7, stored.MaxSftpUsers);
        Assert.Equal(9, stored.MaxFtpUsers);
        Assert.Equal(12, stored.MaxCronEntries);
        Assert.Equal(13, stored.MaxPhpWorkersPerPool);
        Assert.Equal(2, result.Value.AccountCount);
        Assert.Equal(11, result.Value.MaxSites);
    }

    /// <summary>An edit is journalled as a success naming the plan as it was before the edit.</summary>
    [Fact]
    public async Task An_edit_is_journalled_as_a_success_naming_the_plan_as_it_was_before_the_edit()
    {
        await NewHandler().HandleAsync(Same() with { MaxSites = 11 }, CancellationToken.None);

        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(AuditActions.PlanUpdated, entry.Action);
        Assert.Equal("Agency", entry.Subject);
        Assert.True(entry.Succeeded);
        Assert.Equal("203.0.113.7", entry.IpAddress);
    }

    /// <summary>A rename is stored in both name columns and journalled as old to new.</summary>
    [Fact]
    public async Task A_rename_is_stored_in_both_name_columns_and_journalled_as_old_to_new()
    {
        var result = await NewHandler().HandleAsync(Same() with { Name = "  Studio  " }, CancellationToken.None);

        Assert.Equal("Studio", result.Value.DisplayName);
        var stored = await ReloadAsync();
        Assert.Equal("Studio", stored.Name);
        Assert.Equal("Studio", stored.DisplayNameKey);
        Assert.Equal("Agency -> Studio", Assert.Single(_audit.Entries).Subject);
    }

    /// <summary>A rename to a name another plan has is refused and the plan is not changed.</summary>
    [Fact]
    public async Task A_rename_to_a_name_another_plan_has_is_refused_and_the_plan_is_not_changed()
    {
        _dbContext.Plans.Add(Plan.Define(Guid.NewGuid(), "Studio", 5_120, 5, 2, 3, 0, 5, 5));
        await _dbContext.SaveChangesAsync();

        var result = await NewHandler().HandleAsync(Same() with { Name = "studio", MaxSites = 99 }, CancellationToken.None);

        Assert.Equal("PlanNameTaken", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        var stored = await ReloadAsync();
        Assert.Equal("Agency", stored.Name);
        Assert.Equal(10, stored.MaxSites);
        Assert.False(Assert.Single(_audit.Entries).Succeeded);
    }

    /// <summary>A plan may be sent back under its own name, and may change only the case of it.</summary>
    [Fact]
    public async Task A_plan_may_be_sent_back_under_its_own_name_and_may_change_only_the_case_of_it()
    {
        // The name check must exclude the plan being edited, or no plan could ever be saved again.
        var same = await NewHandler().HandleAsync(Same() with { MaxSites = 11 }, CancellationToken.None);
        var recased = await NewHandler().HandleAsync(Same() with { Name = "AGENCY" }, CancellationToken.None);

        Assert.True(same.IsSuccess);
        Assert.True(recased.IsSuccess);
        Assert.Equal("AGENCY", (await ReloadAsync()).Name);
    }

    /// <summary>A shipped plan sent back under the name it shows keeps its translated name.</summary>
    [Fact]
    public async Task A_shipped_plan_sent_back_under_the_name_it_shows_keeps_its_translated_name()
    {
        // A shipped plan stores a resource key and no name, so it reads in each customer's language.
        // Saving the form with the name untouched must not freeze it into one language.
        var shipped = new Plan(Guid.NewGuid(), "PlanStarterName", 5_120, 5, 2, 3, 5, 5);
        _dbContext.Plans.Add(shipped);
        await _dbContext.SaveChangesAsync();

        var result = await NewHandler().HandleAsync(
            new("Starter", 5_120, 6, 2, 3, 0, 5, 5, shipped.Id, "203.0.113.7", "unit-tests"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var stored = await ReloadAsync(shipped.Id);
        Assert.Null(stored.Name);
        Assert.Equal("PlanStarterName", stored.DisplayNameKey);
        Assert.Equal(6, stored.MaxSites);
        Assert.Equal("Starter", result.Value.DisplayName);
    }

    /// <summary>A shipped plan renamed by an operator is journalled under its key, never a translation.</summary>
    [Fact]
    public async Task A_shipped_plan_renamed_by_an_operator_is_journalled_under_its_key_never_a_translation()
    {
        // The journal is never deleted, and an entry that reads differently per language cannot be searched.
        var shipped = new Plan(Guid.NewGuid(), "PlanStarterName", 5_120, 5, 2, 3, 5, 5);
        _dbContext.Plans.Add(shipped);
        await _dbContext.SaveChangesAsync();

        await NewHandler().HandleAsync(
            new("Entry", 5_120, 5, 2, 3, 0, 5, 5, shipped.Id, "203.0.113.7", "unit-tests"), CancellationToken.None);

        Assert.Equal("PlanStarterName -> Entry", Assert.Single(_audit.Entries).Subject);
        Assert.Equal("Entry", (await ReloadAsync(shipped.Id)).Name);
    }

    /// <summary>Lowering the quota below what ONE of several accounts uses is refused whole.</summary>
    [Fact]
    public async Task Lowering_the_quota_below_what_one_of_several_accounts_uses_is_refused_whole()
    {
        await AddAccountAsync("aaa");
        await AddAccountAsync("bbb");
        await AddAccountAsync("ccc");
        // 6 GB in the LAST account: the refusal must come after the earlier accounts were measured
        // and still leave all three untouched.
        _agent.UsedBytes["ccc"] = 6_144 * Mebibyte;

        var result = await NewHandler().HandleAsync(Same() with { DiskQuotaMb = 5_120, MaxSites = 99 }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("PlanQuotaBelowUsage", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        // Nothing was changed: no quota reached any account, and the row — every field of it — is as it was.
        Assert.DoesNotContain(_agent.Calls, call =>
        {
            return call.StartsWith("quota:", StringComparison.Ordinal);
        });
        var stored = await ReloadAsync();
        Assert.Equal(10_240, stored.DiskQuotaMb);
        Assert.Equal(10, stored.MaxSites);
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(AuditActions.PlanUpdated, entry.Action);
        Assert.False(entry.Succeeded);
        Assert.Equal("Agency", entry.Subject);
    }

    /// <summary>An account using exactly the new quota still fits and the quota is pushed to it.</summary>
    [Fact]
    public async Task An_account_using_exactly_the_new_quota_still_fits_and_the_quota_is_pushed_to_it()
    {
        // Full is not over: the refusal test is used &gt; new, and this is the boundary on its allowed side.
        await AddAccountAsync("olive");
        _agent.UsedBytes["olive"] = 5_120 * Mebibyte;

        var result = await NewHandler().HandleAsync(Same() with { DiskQuotaMb = 5_120 }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5_120, (await ReloadAsync()).DiskQuotaMb);
        Assert.Contains($"quota:olive:{5_120 * Mebibyte}", _agent.Calls);
    }

    /// <summary>An account one byte over the new quota is refused.</summary>
    [Fact]
    public async Task An_account_one_byte_over_the_new_quota_is_refused()
    {
        await AddAccountAsync("olive");
        _agent.UsedBytes["olive"] = (5_120 * Mebibyte) + 1;

        var result = await NewHandler().HandleAsync(Same() with { DiskQuotaMb = 5_120 }, CancellationToken.None);

        Assert.Equal("PlanQuotaBelowUsage", result.Error!.Code);
    }

    /// <summary>Lowering the quota to one every account fits under pushes it to every account.</summary>
    [Fact]
    public async Task Lowering_the_quota_to_one_every_account_fits_under_pushes_it_to_every_account()
    {
        await AddAccountAsync("olive");
        await AddAccountAsync("pepper");

        var result = await NewHandler().HandleAsync(Same() with { DiskQuotaMb = 4_096 }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains($"quota:olive:{4_096 * Mebibyte}", _agent.Calls);
        Assert.Contains($"quota:pepper:{4_096 * Mebibyte}", _agent.Calls);
        Assert.Equal(4_096, (await ReloadAsync()).DiskQuotaMb);
    }

    /// <summary>Raising the quota pushes it to every account without reading their usage.</summary>
    [Fact]
    public async Task Raising_the_quota_pushes_it_to_every_account_without_reading_their_usage()
    {
        await AddAccountAsync("olive");
        await AddAccountAsync("pepper");

        var result = await NewHandler().HandleAsync(Same() with { DiskQuotaMb = 20_480 }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["quota:olive:21474836480", "quota:pepper:21474836480"], _agent.Calls);
    }

    /// <summary>Only the accounts on the edited plan are touched.</summary>
    [Fact]
    public async Task Only_the_accounts_on_the_edited_plan_are_touched()
    {
        var other = Plan.Define(Guid.NewGuid(), "Other", 1_024, 1, 1, 1, 0, 0, 1);
        _dbContext.Plans.Add(other);
        await _dbContext.SaveChangesAsync();
        await AddAccountAsync("olive");
        await AddAccountAsync("stranger", other);

        await NewHandler().HandleAsync(Same() with { DiskQuotaMb = 20_480 }, CancellationToken.None);

        Assert.DoesNotContain(_agent.Calls, call =>
        {
            return call.Contains("stranger", StringComparison.Ordinal);
        });
    }

    /// <summary>An edit that leaves the quota alone never asks the agent anything.</summary>
    [Fact]
    public async Task An_edit_that_leaves_the_quota_alone_never_asks_the_agent_anything()
    {
        await AddAccountAsync("olive");

        var result = await NewHandler().HandleAsync(Same() with { Name = "Studio", MaxSites = 11 }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(_agent.Calls);
    }

    /// <summary>A usage the agent cannot read is refused with the agent's own failure, not assumed to fit.</summary>
    [Fact]
    public async Task A_usage_the_agent_cannot_read_is_refused_with_the_agents_own_failure_not_assumed_to_fit()
    {
        // A check that passes when it cannot look is not a check.
        await AddAccountAsync("olive");
        var unavailable = new RecordingAgentAccountsClient(Error.Of("AgentUnavailable", ErrorType.Unavailable));
        var handler = new UpdatePlanCommandHandler(
            _dbContext,
            unavailable,
            new PlanNameDirectory(_dbContext, _displayNames),
            new AccountAuditJournal(_audit, FakeCurrentUser.Admin()),
            _displayNames);

        var result = await handler.HandleAsync(Same() with { DiskQuotaMb = 5_120 }, CancellationToken.None);

        Assert.Equal("AgentUnavailable", result.Error!.Code);
        Assert.Equal(10_240, (await ReloadAsync()).DiskQuotaMb);
        Assert.DoesNotContain(unavailable.Calls, call =>
        {
            return call.StartsWith("quota:", StringComparison.Ordinal);
        });
        Assert.False(Assert.Single(_audit.Entries).Succeeded);
    }

    /// <summary>An agent that refuses one account's quota leaves the plan row as it was.</summary>
    [Fact]
    public async Task An_agent_that_refuses_one_accounts_quota_leaves_the_plan_row_as_it_was()
    {
        await AddAccountAsync("aaa");
        await AddAccountAsync("bbb");
        await AddAccountAsync("ccc");
        _agent.QuotaRefusals["bbb"] = Error.Of("AgentQuotaRefused", ErrorType.Failure);

        var result = await NewHandler().HandleAsync(Same() with { DiskQuotaMb = 20_480 }, CancellationToken.None);

        Assert.Equal("AgentQuotaRefused", result.Error!.Code);
        Assert.Equal(10_240, (await ReloadAsync()).DiskQuotaMb);
        // The account before the refusal already holds the new figure and the one after it was not
        // reached — the documented, convergent outcome: repeating the edit sets the same figures again.
        Assert.Equal(["quota:aaa:21474836480", "quota:bbb:21474836480"], _agent.Calls);
        Assert.False(Assert.Single(_audit.Entries).Succeeded);
    }

    /// <summary>Lowering a count limit below what an account holds is accepted and never asks the agent.</summary>
    [Fact]
    public async Task Lowering_a_count_limit_below_what_an_account_holds_is_accepted_and_never_asks_the_agent()
    {
        // The count limits gate CREATION: an account over a lowered limit keeps what it has and is
        // refused the next one, so there is no outage to refuse — the difference from disk.
        await AddAccountAsync("olive");

        var result = await NewHandler().HandleAsync(
            Same() with { MaxSites = 1, MaxDatabases = 1, MaxSftpUsers = 1, MaxFtpUsers = 0, MaxCronEntries = 0 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, (await ReloadAsync()).MaxSites);
        Assert.Empty(_agent.Calls);
    }
}
