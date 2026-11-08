using Maran.Modules.Accounts.Commands.ChangeAccountPlan;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Events;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.ChangeAccountPlan;

/// <summary>
/// What a move of an account to another plan announces (issue #138): <see cref="AccountPlanChanged"/>
/// once, after the commit, and only when the two plans bound PHP differently.
/// </summary>
public sealed class ChangeAccountPlanPhpLimitsTests : IDisposable
{
    /// <summary>One mebibyte in bytes, the unit a quota is pushed to the agent in.</summary>
    private const ulong Mebibyte = 1024UL * 1024UL;

    /// <summary>Throws an arranged exception on the next save, to force the race the in-memory store cannot.</summary>
    private readonly ArmedSaveFailureInterceptor _saveFailure = new();

    /// <summary>The module's context, in memory.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Everything the handler journals.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>The agent double that owns the account's disk quota and usage.</summary>
    private readonly RecordingAgentAccountsClient _agent = new();

    /// <summary>The plan the account starts on, with the default bounds.</summary>
    private readonly Plan _current = Plan.Define(Guid.NewGuid(), "Current", 10_240, 10, 5, 4, 2, 3, 8);

    /// <summary>A plan selling the same disk with other bounds.</summary>
    private readonly Plan _bounded = Plan.Define(Guid.NewGuid(), "Bounded", 10_240, 10, 5, 4, 2, 3, 8);

    /// <summary>A plan with the same bounds as <see cref="_current"/> and a different name and counts.</summary>
    private readonly Plan _sameBounds = Plan.Define(Guid.NewGuid(), "SameBounds", 10_240, 20, 10, 8, 4, 6, 16);

    /// <summary>A plan with other bounds and less disk than the account holds.</summary>
    private readonly Plan _tooSmall = Plan.Define(Guid.NewGuid(), "TooSmall", 5_120, 2, 1, 1, 0, 0, 4);

    /// <summary>The account every test moves.</summary>
    private readonly Account _account;

    /// <summary>Stores the plans and the account.</summary>
    public ChangeAccountPlanPhpLimitsTests()
    {
        _dbContext = AccountsTestContext.Create(_saveFailure);
        _bounded.ChangePhpLimits(PhpLimits.Of(512, 128, 300, 10_000));
        _tooSmall.ChangePhpLimits(PhpLimits.Of(128, 32, 60, 1_000));
        _dbContext.Plans.AddRange(_current, _bounded, _sameBounds, _tooSmall);
        _account = new Account(
            Guid.NewGuid(),
            "olive",
            "olive.example.com",
            _current.Id,
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

    /// <summary>A move to a plan with other bounds announces the account once.</summary>
    [Fact]
    public async Task A_move_to_a_plan_with_other_bounds_announces_the_account_once()
    {
        var bus = new StubMessageBus();

        var result = await NewHandler(bus).HandleAsync(MoveTo(_bounded.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new AccountPlanChanged(_account.Id), Assert.Single(bus.Invoked));
    }

    /// <summary>A move to a plan with the same bounds announces nothing.</summary>
    [Fact]
    public async Task A_move_to_a_plan_with_the_same_bounds_announces_nothing()
    {
        var bus = new StubMessageBus();

        var result = await NewHandler(bus).HandleAsync(MoveTo(_sameBounds.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(bus.Invoked);
    }

    /// <summary>A move that is refused announces nothing.</summary>
    [Fact]
    public async Task A_move_that_is_refused_announces_nothing()
    {
        var bus = new StubMessageBus();
        _agent.UsedBytes["olive"] = (5_120 * Mebibyte) + 1;

        var result = await NewHandler(bus).HandleAsync(MoveTo(_tooSmall.Id), CancellationToken.None);

        Assert.Equal("AccountPlanQuotaBelowUsage", result.Error!.Code);
        Assert.Empty(bus.Invoked);
    }

    /// <summary>A move whose save fails announces nothing and leaves the account on its plan.</summary>
    [Fact]
    public async Task A_move_whose_save_fails_announces_nothing_and_leaves_the_account_on_its_plan()
    {
        var bus = new StubMessageBus();
        _saveFailure.Next = new DbUpdateConcurrencyException("claimed elsewhere");

        var result = await NewHandler(bus).HandleAsync(MoveTo(_bounded.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(bus.Invoked);
        Assert.Equal(_current.Id, (await _dbContext.Accounts.AsNoTracking().SingleAsync()).PlanId);
    }

    /// <summary>A subscriber that fails is logged and the committed move still succeeds.</summary>
    [Fact]
    public async Task A_subscriber_that_fails_is_logged_and_the_committed_move_still_succeeds()
    {
        var bus = new StubMessageBus(new InvalidOperationException("agent unreachable"));
        var logger = new RecordingLogger<AccountPlanChangeAnnouncer>();

        var result = await NewHandler(bus, logger).HandleAsync(MoveTo(_bounded.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(_bounded.Id, (await _dbContext.Accounts.AsNoTracking().SingleAsync()).PlanId);
        Assert.Single(logger.Messages);
    }

    /// <summary>Builds the handler over this test's context, a bus and a logger for the announcer.</summary>
    /// <param name="bus">The bus the announcement is invoked on.</param>
    /// <param name="logger">Where a subscriber's failure is logged; a discarding one when not stated.</param>
    /// <returns>The handler.</returns>
    private ChangeAccountPlanCommandHandler NewHandler(StubMessageBus bus, RecordingLogger<AccountPlanChangeAnnouncer>? logger = null)
    {
        return AccountHandlerFactory.ChangePlan(_dbContext, FakeCurrentUser.Admin(), _agent, _audit, bus, logger);
    }

    /// <summary>Builds a move of the test account to <paramref name="planId"/>.</summary>
    /// <param name="planId">The plan to move to.</param>
    /// <returns>The command.</returns>
    private ChangeAccountPlanCommand MoveTo(Guid planId)
    {
        return new ChangeAccountPlanCommand(planId, _account.Id, "203.0.113.7", "unit-tests");
    }
}
