using Maran.Modules.Accounts.Commands.TransferAccount;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Maran.Modules.Accounts.Tests.Commands.TransferAccount;

/// <summary>
/// Handing an account to another reseller or to the administrator: the plan is re-paired with the new
/// owner, the new owner's envelope admits it, the host carries the new plan's figures, and the move is
/// announced before the row is written and journalled.
/// </summary>
public sealed class TransferAccountCommandHandlerTests : IDisposable
{
    /// <summary>One mebibyte in bytes, the unit a quota is pushed to the agent in.</summary>
    private const ulong Mebibyte = 1024UL * 1024UL;

    /// <summary>The agent double.</summary>
    private readonly RecordingAgentAccountsClient _agent = new();

    /// <summary>Everything the handler journals.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>The bus the owner change is announced on.</summary>
    private readonly StubMessageBus _bus = new();

    /// <summary>The world the current test built; disposed with the class.</summary>
    private ResellerAccountsWorld? _world;

    /// <inheritdoc />
    public void Dispose()
    {
        _world?.Dispose();
    }

    /// <summary>Builds the world: alpha owns one 1,000 MB account, bravo owns one too.</summary>
    /// <returns>The world.</returns>
    private ResellerAccountsWorld Build()
    {
        _world = new ResellerAccountsWorld();
        return _world;
    }

    /// <summary>Seeds a plan of the second reseller, bigger than the account's current one.</summary>
    /// <param name="world">The world.</param>
    /// <param name="diskQuotaMb">The plan's disk quota.</param>
    /// <returns>The stored plan.</returns>
    private static async Task<Plan> AddBravoPlanAsync(ResellerAccountsWorld world, int diskQuotaMb)
    {
        var plan = Plan.Define(
            Guid.NewGuid(), $"Bravo{diskQuotaMb}", diskQuotaMb, 5, 5, 5, 5, 5, 5, resellerId: world.ResellerB.Id);
        var admin = world.ContextFor(FakeCurrentUser.Admin());
        admin.Plans.Add(plan);
        await admin.SaveChangesAsync();
        return plan;
    }

    /// <summary>Replaces the second reseller's envelope.</summary>
    /// <param name="world">The world.</param>
    /// <param name="maxAccounts">How many accounts it was sold.</param>
    /// <param name="diskPoolMb">Its disk pool.</param>
    private static async Task SetBravoEnvelopeAsync(ResellerAccountsWorld world, int maxAccounts, long diskPoolMb)
    {
        var admin = world.ContextFor(FakeCurrentUser.Admin());
        var bravo = await admin.Resellers.SingleAsync(r => r.Id == world.ResellerB.Id);
        bravo.ChangeEnvelope(ResellerTestData.Envelope(maxAccounts: maxAccounts, diskPoolMb: diskPoolMb));
        await admin.SaveChangesAsync();
    }

    /// <summary>Moves an account as an administrator.</summary>
    /// <param name="world">The world.</param>
    /// <param name="accountId">The account to move.</param>
    /// <param name="resellerId">The new owner, or null for the administrator.</param>
    /// <param name="planId">The plan of the new owner.</param>
    /// <param name="bus">The bus to announce on; the class's stub when not stated.</param>
    /// <param name="interceptor">An interceptor to arm, or null.</param>
    /// <returns>The handler's result.</returns>
    private async Task<Result<AccountDetailDto>> MoveAsync(
        ResellerAccountsWorld world,
        Guid accountId,
        Guid? resellerId,
        Guid planId,
        StubMessageBus? bus = null,
        ArmedSaveFailureInterceptor? interceptor = null)
    {
        var admin = FakeCurrentUser.Admin();
        var dbContext = interceptor is null ? world.ContextFor(admin) : world.ContextFor(admin, interceptor);
        var handler = AccountHandlerFactory.Transfer(dbContext, admin, _agent, _audit, bus ?? _bus);

        return await handler.HandleAsync(
            new TransferAccountCommand(resellerId, planId, accountId, "203.0.113.7", "unit-tests"),
            CancellationToken.None);
    }

    /// <summary>Reads an account back through an administrator's context.</summary>
    /// <param name="world">The world.</param>
    /// <param name="accountId">The account.</param>
    /// <returns>The stored account.</returns>
    private static async Task<Account> StoredAsync(ResellerAccountsWorld world, Guid accountId)
    {
        var admin = world.ContextFor(FakeCurrentUser.Admin());
        return await admin.Accounts.AsNoTracking().SingleAsync(account => account.Id == accountId);
    }

    /// <summary>Builds the failure the database raises for a violated constraint, wrapped as EF Core wraps it.</summary>
    /// <param name="constraint">The constraint that failed.</param>
    /// <returns>The exception.</returns>
    private static DbUpdateException ForeignKeyViolation(string constraint)
    {
        return new DbUpdateException(
            "save failed",
            new PostgresException(
                "constraint violated",
                "ERROR",
                "ERROR",
                PostgresErrorCodes.ForeignKeyViolation,
                constraintName: constraint));
    }

    /// <summary>An administrator hands a reseller's account to another reseller with that reseller's plan.</summary>
    [Fact]
    public async Task An_administrator_hands_an_account_to_another_reseller_with_that_resellers_plan()
    {
        var world = Build();

        var result = await MoveAsync(world, world.AccountA.Id, world.ResellerB.Id, world.PlanB.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(world.ResellerB.Id, result.Value.ResellerId);
        Assert.Equal("bravo", result.Value.ResellerName);
        Assert.Equal(world.PlanB.Id, result.Value.PlanId);
        var stored = await StoredAsync(world, world.AccountA.Id);
        Assert.Equal(world.ResellerB.Id, stored.ResellerId);
        Assert.Equal(world.PlanB.Id, stored.PlanId);
    }

    /// <summary>A move to a plan that bounds PHP differently announces the plan change after the owner change.</summary>
    [Fact]
    public async Task A_move_to_a_plan_with_other_php_bounds_announces_the_plan_change()
    {
        var world = Build();
        var bounded = await AddBravoPlanAsync(world, 2_000);
        var admin = world.ContextFor(FakeCurrentUser.Admin());
        (await admin.Plans.SingleAsync(plan => plan.Id == bounded.Id)).ChangePhpLimits(PhpLimits.Of(512, 128, 300, 10_000));
        await admin.SaveChangesAsync();

        var result = await MoveAsync(world, world.AccountA.Id, world.ResellerB.Id, bounded.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, _bus.Invoked.Count);
        Assert.IsType<AccountOwnerChanged>(_bus.Invoked[0]);
        Assert.Equal(new AccountPlanChanged(world.AccountA.Id, true), _bus.Invoked[1]);
    }

    /// <summary>A move to a plan with the same PHP bounds announces only the owner change.</summary>
    [Fact]
    public async Task A_move_to_a_plan_with_the_same_php_bounds_announces_only_the_owner_change()
    {
        var world = Build();

        var result = await MoveAsync(world, world.AccountA.Id, world.ResellerB.Id, world.PlanB.Id);

        Assert.True(result.IsSuccess);
        Assert.IsType<AccountOwnerChanged>(Assert.Single(_bus.Invoked));
    }

    /// <summary>After a move the account and its plan share one owner, as the pairing requires.</summary>
    [Fact]
    public async Task After_a_move_the_account_and_its_plan_share_one_owner()
    {
        var world = Build();

        await MoveAsync(world, world.AccountA.Id, world.ResellerB.Id, world.PlanB.Id);

        var admin = world.ContextFor(FakeCurrentUser.Admin());
        var account = await admin.Accounts.SingleAsync(a => a.Id == world.AccountA.Id);
        var plan = await admin.Plans.SingleAsync(p => p.Id == account.PlanId);
        Assert.Equal(account.ResellerId, plan.ResellerId);
    }

    /// <summary>The former owner's usage falls and the new owner's rises in the same move.</summary>
    [Fact]
    public async Task The_former_owners_usage_falls_and_the_new_owners_rises()
    {
        var world = Build();
        var bigger = await AddBravoPlanAsync(world, 2_000);

        await MoveAsync(world, world.AccountA.Id, world.ResellerB.Id, bigger.Id);

        var reader = new ResellerUsageReader(world.ContextFor(FakeCurrentUser.Admin()));
        var alpha = await reader.ReadAsync(world.ResellerA.Id, CancellationToken.None);
        var bravo = await reader.ReadAsync(world.ResellerB.Id, CancellationToken.None);
        Assert.Equal(0, alpha.AccountCount);
        Assert.Equal(0, alpha.DiskAllocatedMb);
        Assert.Equal(2, bravo.AccountCount);
        Assert.Equal(1_000 + 2_000, bravo.DiskAllocatedMb);
    }

    /// <summary>The move is announced once, after the row is written, naming the account.</summary>
    [Fact]
    public async Task The_move_is_announced_once_naming_the_account()
    {
        var world = Build();

        await MoveAsync(world, world.AccountA.Id, world.ResellerB.Id, world.PlanB.Id);

        var announced = Assert.IsType<AccountOwnerChanged>(Assert.Single(_bus.Invoked));
        Assert.Equal(world.AccountA.Id, announced.AccountId);
    }

    /// <summary>
    /// The announcement is made when the row already names the new owner (issue #460): the revocation it
    /// triggers can no longer be raced by a session opened under the former owner. The planted break is
    /// the old order, which announced while the row still said the former reseller.
    /// </summary>
    [Fact]
    public async Task The_announcement_is_made_after_the_row_names_the_new_owner()
    {
        var world = Build();
        Guid? ownerWhenAnnounced = null;
        var bus = new StubMessageBus(_ =>
        {
            // A synchronous read: the subscriber is a plain callback, and the row is read from a context
            // of its own, so this sees what the database holds at the moment of the announcement.
            ownerWhenAnnounced = world.ContextFor(FakeCurrentUser.Admin())
                .Accounts.AsNoTracking().Single(account => account.Id == world.AccountA.Id).ResellerId;
        });

        await MoveAsync(world, world.AccountA.Id, world.ResellerB.Id, world.PlanB.Id, bus);

        Assert.Equal(world.ResellerB.Id, ownerWhenAnnounced);
    }

    /// <summary>A subscriber that fails cannot undo a committed move: the move is answered as done.</summary>
    [Fact]
    public async Task A_subscriber_that_fails_does_not_undo_the_committed_move()
    {
        var world = Build();
        var bus = new StubMessageBus(new InvalidOperationException("cannot end the sessions"));

        var result = await MoveAsync(world, world.AccountA.Id, world.ResellerB.Id, world.PlanB.Id, bus);

        Assert.True(result.IsSuccess);
        var stored = await StoredAsync(world, world.AccountA.Id);
        Assert.Equal(world.ResellerB.Id, stored.ResellerId);
        Assert.Equal(world.PlanB.Id, stored.PlanId);
        Assert.Single(_audit.Entries);
    }

    /// <summary>A successful move is journalled with both owners and both plans, against the account.</summary>
    [Fact]
    public async Task A_successful_move_is_journalled_with_both_owners_and_both_plans_against_the_account()
    {
        var world = Build();

        await MoveAsync(world, world.AccountA.Id, world.ResellerB.Id, world.PlanB.Id);

        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(AccountAuditActions.AccountOwnerChanged, entry.Action);
        Assert.True(entry.Succeeded);
        Assert.Equal("alpha-one: alpha -> bravo (plan AlphaPlan -> BravoPlan)", entry.Subject);
        Assert.Equal(world.AccountA.Id, entry.AccountId);
        Assert.Equal("203.0.113.7", entry.IpAddress);
    }

    /// <summary>Handing an account to the administrator clears the reseller and takes the administrator's plan.</summary>
    [Fact]
    public async Task Handing_an_account_to_the_administrator_clears_the_reseller_and_takes_the_administrators_plan()
    {
        var world = Build();

        var result = await MoveAsync(world, world.AccountA.Id, null, world.AdminPlan.Id);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.ResellerId);
        Assert.Null(result.Value.ResellerName);
        var stored = await StoredAsync(world, world.AccountA.Id);
        Assert.Null(stored.ResellerId);
        Assert.Equal(world.AdminPlan.Id, stored.PlanId);
        Assert.Equal("alpha-one: alpha -> (administrator) (plan AlphaPlan -> AdminPlan)", Assert.Single(_audit.Entries).Subject);
    }

    /// <summary>An administrator's account can be handed to a reseller, which then counts it.</summary>
    [Fact]
    public async Task An_administrators_account_can_be_handed_to_a_reseller_which_then_counts_it()
    {
        var world = Build();
        var admin = world.ContextFor(FakeCurrentUser.Admin());
        var own = new Account(
            Guid.NewGuid(), "admin-one", "admin-one.example.com", world.AdminPlan.Id, ResellerTestData.CreatedAt);
        admin.Accounts.Add(own);
        await admin.SaveChangesAsync();

        var result = await MoveAsync(world, own.Id, world.ResellerA.Id, world.PlanA.Id);

        Assert.True(result.IsSuccess);
        var usage = await new ResellerUsageReader(world.ContextFor(FakeCurrentUser.Admin()))
            .ReadAsync(world.ResellerA.Id, CancellationToken.None);
        Assert.Equal(2, usage.AccountCount);
    }

    /// <summary>A different disk quota is pushed to the host before the row is written.</summary>
    [Fact]
    public async Task A_different_disk_quota_is_pushed_to_the_host()
    {
        var world = Build();
        var bigger = await AddBravoPlanAsync(world, 2_000);

        await MoveAsync(world, world.AccountA.Id, world.ResellerB.Id, bigger.Id);

        Assert.Equal([$"quota:alpha-one:{2_000 * Mebibyte}"], _agent.Calls);
    }

    /// <summary>The same disk quota pushes nothing.</summary>
    [Fact]
    public async Task The_same_disk_quota_pushes_nothing_to_the_host()
    {
        var world = Build();

        await MoveAsync(world, world.AccountA.Id, world.ResellerB.Id, world.PlanB.Id);

        Assert.Empty(_agent.Calls);
    }

    /// <summary>A plan whose quota is below the account's usage is refused whole and nothing changes.</summary>
    [Fact]
    public async Task A_plan_whose_quota_is_below_the_accounts_usage_is_refused_whole()
    {
        var world = Build();
        var smaller = await AddBravoPlanAsync(world, 500);
        _agent.UsedBytes["alpha-one"] = 800 * Mebibyte;

        var result = await MoveAsync(world, world.AccountA.Id, world.ResellerB.Id, smaller.Id);

        Assert.Equal("AccountPlanQuotaBelowUsage", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        var stored = await StoredAsync(world, world.AccountA.Id);
        Assert.Equal(world.ResellerA.Id, stored.ResellerId);
        Assert.Equal(world.PlanA.Id, stored.PlanId);
        Assert.False(Assert.Single(_audit.Entries).Succeeded);
    }

    /// <summary>A quota the agent refuses is answered with the agent's failure and leaves the row alone.</summary>
    [Fact]
    public async Task A_quota_the_agent_refuses_leaves_the_row_alone()
    {
        var world = Build();
        var bigger = await AddBravoPlanAsync(world, 2_000);
        _agent.QuotaRefusals["alpha-one"] = Error.Of("AgentSystemFailure", ErrorType.Failure);

        var result = await MoveAsync(world, world.AccountA.Id, world.ResellerB.Id, bigger.Id);

        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        Assert.Equal(world.ResellerA.Id, (await StoredAsync(world, world.AccountA.Id)).ResellerId);
        Assert.False(Assert.Single(_audit.Entries).Succeeded);
    }

    /// <summary>A plan of another owner than the new one is refused as a mismatch, before the host is touched.</summary>
    [Fact]
    public async Task A_plan_of_another_owner_than_the_new_one_is_refused_as_a_mismatch()
    {
        var world = Build();

        var result = await MoveAsync(world, world.AccountA.Id, world.ResellerB.Id, world.PlanA.Id);

        Assert.Equal("PlanOwnerMismatch", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(world.ResellerA.Id, (await StoredAsync(world, world.AccountA.Id)).ResellerId);
        Assert.Empty(_agent.Calls);
        Assert.Empty(_bus.Invoked);
        Assert.False(Assert.Single(_audit.Entries).Succeeded);
    }

    /// <summary>An administrator's plan for a reseller target, and a reseller's plan for the administrator, are both mismatches.</summary>
    [Fact]
    public async Task A_plan_of_the_administrator_for_a_reseller_and_the_reverse_are_both_mismatches()
    {
        var world = Build();

        var toReseller = await MoveAsync(world, world.AccountA.Id, world.ResellerB.Id, world.AdminPlan.Id);
        var toAdministrator = await MoveAsync(world, world.AccountA.Id, null, world.PlanA.Id);

        Assert.Equal("PlanOwnerMismatch", toReseller.Error!.Code);
        Assert.Equal("PlanOwnerMismatch", toAdministrator.Error!.Code);
    }

    /// <summary>A reseller that does not exist is answered not found, and the move changes nothing.</summary>
    [Fact]
    public async Task A_reseller_that_does_not_exist_is_answered_not_found()
    {
        var world = Build();

        var result = await MoveAsync(world, world.AccountA.Id, Guid.NewGuid(), world.PlanB.Id);

        Assert.Equal("ResellerNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal(world.ResellerA.Id, (await StoredAsync(world, world.AccountA.Id)).ResellerId);
    }

    /// <summary>A plan that does not exist is answered not found.</summary>
    [Fact]
    public async Task A_plan_that_does_not_exist_is_answered_not_found()
    {
        var world = Build();

        var result = await MoveAsync(world, world.AccountA.Id, world.ResellerB.Id, Guid.NewGuid());

        Assert.Equal("PlanNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    /// <summary>An account that does not exist is answered not found and journalled under the supplied id.</summary>
    [Fact]
    public async Task An_account_that_does_not_exist_is_answered_not_found_and_journalled_under_the_supplied_id()
    {
        var world = Build();
        var missing = Guid.NewGuid();

        var result = await MoveAsync(world, missing, world.ResellerB.Id, world.PlanB.Id);

        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        var entry = Assert.Single(_audit.Entries);
        Assert.False(entry.Succeeded);
        Assert.Equal(missing.ToString("D"), entry.Subject);
    }

    /// <summary>A new owner that has reached its account limit refuses the account.</summary>
    [Fact]
    public async Task A_new_owner_that_has_reached_its_account_limit_refuses_the_account()
    {
        var world = Build();
        await SetBravoEnvelopeAsync(world, maxAccounts: 1, diskPoolMb: 100_000);

        var result = await MoveAsync(world, world.AccountA.Id, world.ResellerB.Id, world.PlanB.Id);

        Assert.Equal("ResellerAccountLimitReached", result.Error!.Code);
        Assert.Equal(world.ResellerA.Id, (await StoredAsync(world, world.AccountA.Id)).ResellerId);
        Assert.Empty(_bus.Invoked);
    }

    /// <summary>A new owner with exactly one slot left accepts the account.</summary>
    [Fact]
    public async Task A_new_owner_with_exactly_one_slot_left_accepts_the_account()
    {
        var world = Build();
        await SetBravoEnvelopeAsync(world, maxAccounts: 2, diskPoolMb: 100_000);

        var result = await MoveAsync(world, world.AccountA.Id, world.ResellerB.Id, world.PlanB.Id);

        Assert.True(result.IsSuccess);
    }

    /// <summary>A plan that takes the new owner one megabyte past its disk pool is refused.</summary>
    [Fact]
    public async Task A_plan_that_takes_the_new_owner_one_megabyte_past_its_pool_is_refused()
    {
        var world = Build();
        await SetBravoEnvelopeAsync(world, maxAccounts: 10, diskPoolMb: 1_999);

        var result = await MoveAsync(world, world.AccountA.Id, world.ResellerB.Id, world.PlanB.Id);

        Assert.Equal("ResellerDiskPoolExceeded", result.Error!.Code);
        Assert.Equal(world.ResellerA.Id, (await StoredAsync(world, world.AccountA.Id)).ResellerId);
        Assert.Empty(_agent.Calls);
    }

    /// <summary>A plan that brings the new owner's pool exactly to its size is accepted.</summary>
    [Fact]
    public async Task A_plan_that_brings_the_new_owners_pool_exactly_to_its_size_is_accepted()
    {
        var world = Build();
        await SetBravoEnvelopeAsync(world, maxAccounts: 10, diskPoolMb: 2_000);

        var result = await MoveAsync(world, world.AccountA.Id, world.ResellerB.Id, world.PlanB.Id);

        Assert.True(result.IsSuccess);
    }

    /// <summary>Handing an account to the administrator is admitted against no envelope at all.</summary>
    [Fact]
    public async Task Handing_an_account_to_the_administrator_is_admitted_against_no_envelope()
    {
        var world = Build();
        await SetBravoEnvelopeAsync(world, maxAccounts: 0, diskPoolMb: 0);

        var result = await MoveAsync(world, world.AccountB.Id, null, world.AdminPlan.Id);

        Assert.True(result.IsSuccess);
    }

    /// <summary>Naming the owner and the plan the account already has returns it unchanged and records nothing.</summary>
    [Fact]
    public async Task Naming_the_owner_and_the_plan_the_account_already_has_returns_it_unchanged()
    {
        var world = Build();

        var result = await MoveAsync(world, world.AccountA.Id, world.ResellerA.Id, world.PlanA.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(world.ResellerA.Id, result.Value.ResellerId);
        Assert.Empty(_audit.Entries);
        Assert.Empty(_bus.Invoked);
        Assert.Empty(_agent.Calls);
    }

    /// <summary>Naming the owner the account already has with another plan is refused: a plan change is its own operation.</summary>
    [Fact]
    public async Task Naming_the_owner_the_account_already_has_with_another_plan_is_refused()
    {
        var world = Build();

        var result = await MoveAsync(world, world.AccountA.Id, world.ResellerA.Id, world.LargePlanA.Id);

        Assert.Equal("AccountOwnerUnchanged", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(world.PlanA.Id, (await StoredAsync(world, world.AccountA.Id)).PlanId);
        Assert.False(Assert.Single(_audit.Entries).Succeeded);
    }

    /// <summary>A move that loses a race for the new owner's allocation is refused and puts the host back.</summary>
    [Fact]
    public async Task A_move_that_loses_a_race_for_the_new_owners_allocation_puts_the_host_back()
    {
        var world = Build();
        var bigger = await AddBravoPlanAsync(world, 2_000);
        var interceptor = new ArmedSaveFailureInterceptor { Next = new DbUpdateConcurrencyException("race") };

        var result = await MoveAsync(world, world.AccountA.Id, world.ResellerB.Id, bigger.Id, interceptor: interceptor);

        Assert.Equal("ResellerAllocationChanged", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(
            [$"quota:alpha-one:{2_000 * Mebibyte}", $"quota:alpha-one:{1_000 * Mebibyte}"],
            _agent.Calls);
        Assert.Equal(world.ResellerA.Id, (await StoredAsync(world, world.AccountA.Id)).ResellerId);
        Assert.False(Assert.Single(_audit.Entries).Succeeded);
    }

    /// <summary>A plan deleted between the check and the save is answered plan not found.</summary>
    [Fact]
    public async Task A_plan_deleted_between_the_check_and_the_save_is_answered_plan_not_found()
    {
        var world = Build();
        var interceptor = new ArmedSaveFailureInterceptor { Next = ForeignKeyViolation("FK_Accounts_Plans_PlanId") };

        var result = await MoveAsync(world, world.AccountA.Id, world.ResellerB.Id, world.PlanB.Id, interceptor: interceptor);

        Assert.Equal("PlanNotFound", result.Error!.Code);
        Assert.False(Assert.Single(_audit.Entries).Succeeded);
    }

    /// <summary>A reseller deleted between the check and the save is answered reseller not found.</summary>
    [Fact]
    public async Task A_reseller_deleted_between_the_check_and_the_save_is_answered_reseller_not_found()
    {
        var world = Build();
        var interceptor = new ArmedSaveFailureInterceptor
        {
            Next = ForeignKeyViolation("FK_Accounts_Resellers_ResellerId"),
        };

        var result = await MoveAsync(world, world.AccountA.Id, world.ResellerB.Id, world.PlanB.Id, interceptor: interceptor);

        Assert.Equal("ResellerNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    /// <summary>A foreign key the move did not touch propagates instead of being called a missing plan.</summary>
    [Fact]
    public async Task A_foreign_key_the_move_did_not_touch_propagates()
    {
        // The inverse control: a catch that took every 23503 for a missing plan would pass the two tests above.
        var world = Build();
        var interceptor = new ArmedSaveFailureInterceptor { Next = ForeignKeyViolation("FK_Somewhere_Else") };

        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            await MoveAsync(world, world.AccountA.Id, world.ResellerB.Id, world.PlanB.Id, interceptor: interceptor);
        });
    }
}
