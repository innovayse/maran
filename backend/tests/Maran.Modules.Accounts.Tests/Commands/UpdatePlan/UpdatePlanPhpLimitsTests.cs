using Maran.Modules.Accounts.Commands.UpdatePlan;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.UpdatePlan;

/// <summary>
/// How a plan edit treats the PHP bounds (issue #138): stated bounds replace the plan's, omitted
/// ones leave them, and a changed bound is announced once per account on the plan after the save.
/// </summary>
public sealed class UpdatePlanPhpLimitsTests : IDisposable
{
    /// <summary>The bounds the plan under edit starts with: unlike the default and unlike every edit below.</summary>
    private static readonly PhpLimitsDto Current = new(192, 48, 90, 2_000);

    /// <summary>Throws an arranged exception on the next save, to force the race the in-memory store cannot.</summary>
    private readonly ArmedSaveFailureInterceptor _saveFailure = new();

    /// <summary>The module's context, in memory.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Everything the handler journals.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>The agent double; no quota or ceiling here ever changes, so it is never called.</summary>
    private readonly RecordingAgentAccountsClient _agent = new();

    /// <summary>The display names of the plans the panel ships.</summary>
    private readonly StubDisplayNames _displayNames = new();

    /// <summary>The plan every test edits.</summary>
    private readonly Plan _plan = Plan.Define(Guid.NewGuid(), "Agency", 10_240, 10, 5, 4, 2, 3, 8);

    /// <summary>Another plan, whose account must never be announced.</summary>
    private readonly Plan _other = Plan.Define(Guid.NewGuid(), "Other", 10_240, 10, 5, 4, 2, 3, 8);

    /// <summary>The two accounts on the plan under edit.</summary>
    private readonly List<Account> _onPlan = [];

    /// <summary>The account on the other plan.</summary>
    private readonly Account _onOther;

    /// <summary>Stores the plans and the accounts the tests edit around.</summary>
    public UpdatePlanPhpLimitsTests()
    {
        _dbContext = AccountsTestContext.Create(_saveFailure);
        _plan.ChangePhpLimits(PhpLimitsMapper.ToDomain(Current));
        _dbContext.Plans.AddRange(_plan, _other);
        _onPlan.Add(NewAccount("first", _plan));
        _onPlan.Add(NewAccount("second", _plan));
        _onOther = NewAccount("third", _other);
        _dbContext.Accounts.AddRange(_onPlan);
        _dbContext.Accounts.Add(_onOther);
        _dbContext.SaveChanges();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _dbContext.Dispose();
    }

    /// <summary>Changed bounds are stored, reported, and announced once for each account on the plan and no other.</summary>
    [Fact]
    public async Task Changed_bounds_are_stored_reported_and_announced_once_for_each_account_on_the_plan()
    {
        var bus = new StubMessageBus();
        var stated = new PhpLimitsDto(512, 128, 300, 10_000);

        var result = await NewHandler(bus).HandleAsync(Edit(stated), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(stated, result.Value!.PhpLimits);
        Assert.Equal(PhpLimitsMapper.ToDomain(stated), (await ReloadAsync()).PhpLimits);
        Assert.Equal(2, bus.Invoked.Count);
        foreach (var account in _onPlan)
        {
            Assert.Contains(new AccountPlanChanged(account.Id), bus.Invoked);
        }

        Assert.DoesNotContain(new AccountPlanChanged(_onOther.Id), bus.Invoked);
    }

    /// <summary>Stating the bounds the plan already has announces nothing.</summary>
    [Fact]
    public async Task Stating_the_bounds_the_plan_already_has_announces_nothing()
    {
        var bus = new StubMessageBus();

        var result = await NewHandler(bus).HandleAsync(Edit(Current with { }), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(bus.Invoked);
    }

    /// <summary>A request that leaves the bounds out keeps the plan's own and announces nothing.</summary>
    [Fact]
    public async Task A_request_that_leaves_the_bounds_out_keeps_the_plans_own_and_announces_nothing()
    {
        var bus = new StubMessageBus();

        var result = await NewHandler(bus).HandleAsync(Edit(null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Current, result.Value!.PhpLimits);
        Assert.Equal(PhpLimitsMapper.ToDomain(Current), (await ReloadAsync()).PhpLimits);
        Assert.Empty(bus.Invoked);
    }

    /// <summary>An edit whose save fails changes no bound and announces nothing.</summary>
    [Fact]
    public async Task An_edit_whose_save_fails_changes_no_bound_and_announces_nothing()
    {
        var bus = new StubMessageBus();
        _saveFailure.Next = new DbUpdateConcurrencyException("claimed elsewhere");

        var result = await NewHandler(bus).HandleAsync(Edit(new PhpLimitsDto(512, 128, 300, 10_000)), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(PhpLimitsMapper.ToDomain(Current), (await ReloadAsync()).PhpLimits);
        Assert.Empty(bus.Invoked);
    }

    /// <summary>A subscriber that fails is logged and the committed edit still succeeds.</summary>
    [Fact]
    public async Task A_subscriber_that_fails_is_logged_and_the_committed_edit_still_succeeds()
    {
        var bus = new StubMessageBus(new InvalidOperationException("agent unreachable"));
        var logger = new RecordingLogger<AccountPlanChangeAnnouncer>();
        var stated = new PhpLimitsDto(512, 128, 300, 10_000);

        var result = await NewHandler(bus, logger).HandleAsync(Edit(stated), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PhpLimitsMapper.ToDomain(stated), (await ReloadAsync()).PhpLimits);
        Assert.Equal(2, logger.Messages.Count);
    }

    /// <summary>A stated bound out of range is refused by the validator on that bound.</summary>
    [Theory]
    [InlineData(0, nameof(PhpLimitsDto.MemoryLimitMaxMb))]
    [InlineData(PhpSettingCeilings.MaxSizeMb + 1, nameof(PhpLimitsDto.UploadMaxMb))]
    [InlineData(PhpSettingCeilings.MaxExecutionSeconds + 1, nameof(PhpLimitsDto.MaxExecutionSeconds))]
    [InlineData(PhpSettingCeilings.MaxInputVars + 1, nameof(PhpLimitsDto.MaxInputVars))]
    public async Task A_stated_bound_out_of_range_is_refused_by_the_validator_on_that_bound(int value, string bound)
    {
        var stated = bound switch
        {
            nameof(PhpLimitsDto.MemoryLimitMaxMb) => Current with { MemoryLimitMaxMb = value },
            nameof(PhpLimitsDto.UploadMaxMb) => Current with { UploadMaxMb = value },
            nameof(PhpLimitsDto.MaxExecutionSeconds) => Current with { MaxExecutionSeconds = value },
            _ => Current with { MaxInputVars = value },
        };

        var verdict = await new UpdatePlanCommandValidator().ValidateAsync(Edit(stated));

        var error = Assert.Single(verdict.Errors);
        Assert.Equal($"{nameof(UpdatePlanCommand.PhpLimits)}.{bound}", error.PropertyName);
    }

    /// <summary>An edit that leaves the bounds out is valid.</summary>
    [Fact]
    public async Task An_edit_that_leaves_the_bounds_out_is_valid()
    {
        var verdict = await new UpdatePlanCommandValidator().ValidateAsync(Edit(null));

        Assert.True(verdict.IsValid);
    }

    /// <summary>Builds an account on a plan.</summary>
    /// <param name="name">The account's system user name.</param>
    /// <param name="plan">The plan it is on.</param>
    /// <returns>The account.</returns>
    private static Account NewAccount(string name, Plan plan)
    {
        return new Account(
            Guid.NewGuid(), name, $"{name}.example.com", plan.Id, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    /// <summary>Builds the handler over this test's context, a bus and a logger for the announcer.</summary>
    /// <param name="bus">The bus the announcement is invoked on.</param>
    /// <param name="logger">Where a subscriber's failure is logged; a discarding one when not stated.</param>
    /// <returns>The handler.</returns>
    private UpdatePlanCommandHandler NewHandler(StubMessageBus bus, RecordingLogger<AccountPlanChangeAnnouncer>? logger = null)
    {
        return PlanHandlerFactory.Update(_dbContext, FakeCurrentUser.Admin(), _audit, _displayNames, _agent, bus, logger);
    }

    /// <summary>Builds an edit that changes nothing but the PHP bounds it states.</summary>
    /// <param name="phpLimits">The bounds to state, or <c>null</c> for a request that left them out.</param>
    /// <returns>The command.</returns>
    private UpdatePlanCommand Edit(PhpLimitsDto? phpLimits)
    {
        return new UpdatePlanCommand(
            "Agency", 10_240, 10, 5, 4, 2, 3, 8,
            ResourceCeilingTestValues.Unlimited, ResourceCeilingTestValues.Unlimited, ResourceCeilingTestValues.Unlimited,
            [AccountFeature.Mail],
            phpLimits,
            PlanId: _plan.Id,
            IpAddress: "203.0.113.7",
            UserAgent: "unit-tests");
    }

    /// <summary>Re-reads the plan from the store, bypassing what the context already tracks.</summary>
    /// <returns>The stored plan.</returns>
    private async Task<Plan> ReloadAsync()
    {
        return await _dbContext.Plans.AsNoTracking().SingleAsync(plan => plan.Id == _plan.Id);
    }
}
