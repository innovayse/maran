using Maran.Modules.Accounts.Commands.ChangeAccountPlan;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.ChangeAccountPlan;

/// <summary>
/// Moving an account between plans as a reseller: only its own accounts and plans exist for it, the
/// plan must have the account's owner, and the move must fit the owner's disk pool, inclusively.
/// </summary>
public sealed class ChangeAccountPlanResellerTests : IDisposable
{
    /// <summary>One mebibyte in bytes, the unit a quota is pushed to the agent in.</summary>
    private const ulong Mebibyte = 1024UL * 1024UL;

    /// <summary>The agent double.</summary>
    private readonly RecordingAgentAccountsClient _agent = new();

    /// <summary>Everything the handler journals.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>The world the current test built; disposed with the class.</summary>
    private ResellerAccountsWorld? _world;

    /// <inheritdoc />
    public void Dispose()
    {
        _world?.Dispose();
    }

    /// <summary>Builds the world; the first reseller owns one 1,000 MB account.</summary>
    /// <param name="diskPoolMb">The disk pool of the first reseller.</param>
    /// <returns>The world.</returns>
    private ResellerAccountsWorld Build(long diskPoolMb = 100_000)
    {
        _world = new ResellerAccountsWorld(ResellerTestData.Envelope(diskPoolMb: diskPoolMb));
        return _world;
    }

    /// <summary>Moves an account as <paramref name="caller"/>.</summary>
    /// <param name="world">The world.</param>
    /// <param name="caller">The caller the move runs as.</param>
    /// <param name="accountId">The account to move.</param>
    /// <param name="planId">The plan to move it to.</param>
    /// <param name="interceptor">An interceptor to arm, or null.</param>
    /// <returns>The handler's result.</returns>
    private async Task<Result<AccountDetailDto>> MoveAsync(
        ResellerAccountsWorld world,
        ICurrentUser caller,
        Guid accountId,
        Guid planId,
        ArmedSaveFailureInterceptor? interceptor = null)
    {
        var dbContext = interceptor is null ? world.ContextFor(caller) : world.ContextFor(caller, interceptor);
        var handler = AccountHandlerFactory.ChangePlan(dbContext, caller, _agent, _audit);

        return await handler.HandleAsync(
            new ChangeAccountPlanCommand(planId, accountId, "203.0.113.7", "unit-tests"),
            CancellationToken.None);
    }

    /// <summary>Reads an account's stored plan back through an administrator's context.</summary>
    /// <param name="world">The world.</param>
    /// <param name="accountId">The account.</param>
    /// <returns>The stored plan id.</returns>
    private static async Task<Guid> StoredPlanAsync(ResellerAccountsWorld world, Guid accountId)
    {
        var admin = world.ContextFor(FakeCurrentUser.Admin());
        return (await admin.Accounts.AsNoTracking().SingleAsync(account => account.Id == accountId)).PlanId;
    }

    /// <summary>A reseller moves its own account to its own larger plan and the answer names the reseller.</summary>
    [Fact]
    public async Task A_reseller_moves_its_own_account_to_its_own_larger_plan()
    {
        var world = Build();

        var result = await MoveAsync(world, world.UserA, world.AccountA.Id, world.LargePlanA.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal("alpha", result.Value.ResellerName);
        Assert.Equal(world.LargePlanA.Id, await StoredPlanAsync(world, world.AccountA.Id));
        Assert.Equal([$"quota:alpha-one:{2_000 * Mebibyte}"], _agent.Calls);
        var entry = Assert.Single(_audit.Entries);
        Assert.True(entry.Succeeded);
        Assert.Equal("alpha-login", entry.ActorUsername);
        Assert.Equal(AccountAuditActions.AccountPlanChanged, entry.Action);
    }

    /// <summary>The account keeps its owner when it moves, so it still shares an owner with its plan.</summary>
    [Fact]
    public async Task A_move_leaves_the_account_with_the_same_owner_as_its_plan()
    {
        var world = Build();

        await MoveAsync(world, world.UserA, world.AccountA.Id, world.LargePlanA.Id);

        var admin = world.ContextFor(FakeCurrentUser.Admin());
        var account = await admin.Accounts.SingleAsync(a => a.Id == world.AccountA.Id);
        var plan = await admin.Plans.SingleAsync(p => p.Id == account.PlanId);
        Assert.Equal(account.ResellerId, plan.ResellerId);
        Assert.Equal(world.ResellerA.Id, account.ResellerId);
    }

    /// <summary>A reseller moving another reseller's account is answered exactly as for an account that does not exist.</summary>
    [Fact]
    public async Task A_reseller_moving_another_resellers_account_is_answered_as_for_one_that_does_not_exist()
    {
        var world = Build();

        var foreign = await MoveAsync(world, world.UserA, world.AccountB.Id, world.PlanA.Id);
        var missing = await MoveAsync(world, world.UserA, Guid.NewGuid(), world.PlanA.Id);

        Assert.Equal("AccountNotFound", foreign.Error!.Code);
        Assert.Equal(ErrorType.NotFound, foreign.Error.Type);
        Assert.Equal(missing.Error!.Code, foreign.Error.Code);
        Assert.Equal(world.PlanB.Id, await StoredPlanAsync(world, world.AccountB.Id));
        Assert.Empty(_agent.Calls);
    }

    /// <summary>A reseller moving its account to another reseller's plan is answered plan not found and changes nothing.</summary>
    [Fact]
    public async Task A_reseller_moving_its_account_to_another_resellers_plan_is_answered_plan_not_found()
    {
        var world = Build();

        var result = await MoveAsync(world, world.UserA, world.AccountA.Id, world.PlanB.Id);

        Assert.Equal("PlanNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal(world.PlanA.Id, await StoredPlanAsync(world, world.AccountA.Id));
        Assert.False(Assert.Single(_audit.Entries).Succeeded);
    }

    /// <summary>A reseller moving its account to an administrator's plan is answered plan not found, because it cannot see it.</summary>
    [Fact]
    public async Task A_reseller_moving_its_account_to_an_administrators_plan_is_answered_plan_not_found()
    {
        var world = Build();

        var result = await MoveAsync(world, world.UserA, world.AccountA.Id, world.AdminPlan.Id);

        Assert.Equal("PlanNotFound", result.Error!.Code);
        Assert.Equal(world.PlanA.Id, await StoredPlanAsync(world, world.AccountA.Id));
    }

    /// <summary>An administrator moving a reseller's account to an administrator's plan is refused as a plan owner mismatch.</summary>
    [Fact]
    public async Task An_administrator_moving_a_resellers_account_to_an_administrators_plan_is_refused_as_a_mismatch()
    {
        var world = Build();

        var result = await MoveAsync(world, FakeCurrentUser.Admin(), world.AccountA.Id, world.AdminPlan.Id);

        Assert.Equal("PlanOwnerMismatch", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(world.PlanA.Id, await StoredPlanAsync(world, world.AccountA.Id));
        Assert.Empty(_agent.Calls);
    }

    /// <summary>An administrator moving a reseller's account to another reseller's plan is refused as a plan owner mismatch.</summary>
    [Fact]
    public async Task An_administrator_moving_a_resellers_account_to_another_resellers_plan_is_refused_as_a_mismatch()
    {
        var world = Build();

        var result = await MoveAsync(world, FakeCurrentUser.Admin(), world.AccountA.Id, world.PlanB.Id);

        Assert.Equal("PlanOwnerMismatch", result.Error!.Code);
    }

    /// <summary>An administrator moving an administrator's account to a reseller's plan is refused as a plan owner mismatch.</summary>
    [Fact]
    public async Task An_administrator_moving_its_own_account_to_a_resellers_plan_is_refused_as_a_mismatch()
    {
        var world = Build();
        var admin = world.ContextFor(FakeCurrentUser.Admin());
        var adminAccount = new Account(
            Guid.NewGuid(), "admin-one", "admin-one.example.com", world.AdminPlan.Id, ResellerTestData.CreatedAt);
        admin.Accounts.Add(adminAccount);
        await admin.SaveChangesAsync();

        var result = await MoveAsync(world, FakeCurrentUser.Admin(), adminAccount.Id, world.PlanA.Id);

        Assert.Equal("PlanOwnerMismatch", result.Error!.Code);
    }

    /// <summary>A move that brings the pool exactly to its size is accepted.</summary>
    [Fact]
    public async Task A_move_that_brings_the_pool_exactly_to_its_size_is_accepted()
    {
        var world = Build(diskPoolMb: 2_000);

        var result = await MoveAsync(world, world.UserA, world.AccountA.Id, world.LargePlanA.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(world.LargePlanA.Id, await StoredPlanAsync(world, world.AccountA.Id));
    }

    /// <summary>A move one megabyte over the pool is refused, journalled, and leaves the account and the host as they were.</summary>
    [Fact]
    public async Task A_move_one_megabyte_over_the_pool_is_refused_and_leaves_the_account_and_the_host_as_they_were()
    {
        var world = Build(diskPoolMb: 1_999);

        var result = await MoveAsync(world, world.UserA, world.AccountA.Id, world.LargePlanA.Id);

        Assert.Equal("ResellerDiskPoolExceeded", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(world.PlanA.Id, await StoredPlanAsync(world, world.AccountA.Id));
        Assert.Empty(_agent.Calls);
        Assert.False(Assert.Single(_audit.Entries).Succeeded);
    }

    /// <summary>A reseller whose envelope was lowered below its usage can still move an account to a smaller plan.</summary>
    [Fact]
    public async Task A_reseller_over_its_pool_can_still_move_an_account_to_a_smaller_plan()
    {
        var world = Build(diskPoolMb: 100);
        var smaller = Plan.Define(
            Guid.NewGuid(), "AlphaSmall", 500, 5, 5, 5, 5, 5, 5, resellerId: world.ResellerA.Id);
        var admin = world.ContextFor(FakeCurrentUser.Admin());
        admin.Plans.Add(smaller);
        await admin.SaveChangesAsync();

        var result = await MoveAsync(world, world.UserA, world.AccountA.Id, smaller.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(smaller.Id, await StoredPlanAsync(world, world.AccountA.Id));
    }

    /// <summary>A suspended reseller's live token cannot move an account.</summary>
    [Fact]
    public async Task A_suspended_resellers_live_token_cannot_move_an_account()
    {
        var world = Build();
        var admin = world.ContextFor(FakeCurrentUser.Admin());
        (await admin.Resellers.SingleAsync(r => r.Id == world.ResellerA.Id)).Suspend();
        await admin.SaveChangesAsync();

        var result = await MoveAsync(world, world.UserA, world.AccountA.Id, world.LargePlanA.Id);

        Assert.Equal("ResellerSuspended", result.Error!.Code);
        Assert.Equal(world.PlanA.Id, await StoredPlanAsync(world, world.AccountA.Id));
        Assert.Empty(_agent.Calls);
        Assert.False(Assert.Single(_audit.Entries).Succeeded);
    }

    /// <summary>A move that loses a race for the pool is refused as the pool and puts the host back on the current quota.</summary>
    [Fact]
    public async Task A_move_that_loses_a_race_for_the_pool_is_refused_and_puts_the_host_back_on_the_current_quota()
    {
        var world = Build();
        var interceptor = new ArmedSaveFailureInterceptor { Next = new DbUpdateConcurrencyException("race") };

        var result = await MoveAsync(world, world.UserA, world.AccountA.Id, world.LargePlanA.Id, interceptor);

        Assert.Equal("ResellerDiskPoolExceeded", result.Error!.Code);
        Assert.Equal(
            [$"quota:alpha-one:{2_000 * Mebibyte}", $"quota:alpha-one:{1_000 * Mebibyte}"],
            _agent.Calls);
        Assert.Equal(world.PlanA.Id, await StoredPlanAsync(world, world.AccountA.Id));
        Assert.False(Assert.Single(_audit.Entries).Succeeded);
    }
}
