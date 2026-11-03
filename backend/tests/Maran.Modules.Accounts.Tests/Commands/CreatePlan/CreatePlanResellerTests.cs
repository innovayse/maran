using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.CreatePlan;

/// <summary>
/// Behavioral contract of <see cref="Maran.Modules.Accounts.Commands.CreatePlan.CreatePlanCommandHandler"/>
/// for a reseller: the plan is the reseller's own, taken from its token and never from the request; a
/// suspended reseller cannot define one; and the plan count of the envelope is inclusive.
/// </summary>
public sealed class CreatePlanResellerTests : IDisposable
{
    /// <summary>The reseller, its rival and the contexts both are seen through.</summary>
    private readonly ResellerPlanScenario _scenario = new(ResellerTestData.Envelope(maxPlans: 2));

    /// <inheritdoc />
    public void Dispose()
    {
        _scenario.Dispose();
    }

    /// <summary>A plan a reseller creates is stamped with the reseller's id from its token and shows its name.</summary>
    [Fact]
    public async Task A_plan_a_reseller_creates_is_stamped_with_the_resellers_id_from_its_token()
    {
        var result = await _scenario.CreateAsReseller().HandleAsync(ResellerPlanScenario.Create(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(_scenario.ResellerId, result.Value.ResellerId);
        Assert.Equal("acme", result.Value.ResellerName);
        var stored = await _scenario.ReadPlanAsync(result.Value.Id);
        Assert.Equal(_scenario.ResellerId, stored!.ResellerId);
    }

    /// <summary>A plan an administrator creates belongs to the administrator, not to any reseller.</summary>
    [Fact]
    public async Task A_plan_an_administrator_creates_belongs_to_the_administrator()
    {
        var result = await _scenario.CreateAsAdministrator().HandleAsync(
            ResellerPlanScenario.Create(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.ResellerId);
        Assert.Null(result.Value.ResellerName);
        Assert.Null((await _scenario.ReadPlanAsync(result.Value.Id))!.ResellerId);
    }

    /// <summary>A reseller's creation is journalled as a success with the reseller as the actor.</summary>
    [Fact]
    public async Task A_resellers_creation_is_journalled_as_a_success_with_the_reseller_as_the_actor()
    {
        await _scenario.CreateAsReseller().HandleAsync(ResellerPlanScenario.Create(), CancellationToken.None);

        var entry = Assert.Single(_scenario.Audit.Entries);
        Assert.Equal(AccountAuditActions.PlanCreated, entry.Action);
        Assert.Equal("Gold", entry.Subject);
        Assert.True(entry.Succeeded);
        Assert.Equal(((ResellerCaller)_scenario.ResellerUser.Caller).UserId, entry.ActorUserId);
        Assert.Equal("reseller", entry.ActorUsername);
    }

    /// <summary>A successful creation claims the reseller's row so a concurrent creation is refused at the save.</summary>
    [Fact]
    public async Task A_successful_creation_claims_the_resellers_row()
    {
        var before = (await _scenario.ReadResellerAsync()).AllocationRevision;

        await _scenario.CreateAsReseller().HandleAsync(ResellerPlanScenario.Create(), CancellationToken.None);

        Assert.Equal(before + 1, (await _scenario.ReadResellerAsync()).AllocationRevision);
    }

    /// <summary>A refused creation claims nothing.</summary>
    [Fact]
    public async Task A_refused_creation_claims_nothing()
    {
        await _scenario.CreateAsReseller().HandleAsync(
            ResellerPlanScenario.Create() with { DiskQuotaMb = 999_999 }, CancellationToken.None);

        Assert.Equal(0, (await _scenario.ReadResellerAsync()).AllocationRevision);
    }

    /// <summary>The plan that fills the last slot of the envelope is accepted.</summary>
    [Fact]
    public async Task The_plan_that_fills_the_last_slot_of_the_envelope_is_accepted()
    {
        _scenario.AddPlan("First", _scenario.ResellerId);

        var result = await _scenario.CreateAsReseller().HandleAsync(
            ResellerPlanScenario.Create("Second"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, await _scenario.Seed.Plans.CountAsync(plan => plan.ResellerId == _scenario.ResellerId));
    }

    /// <summary>A plan beyond the plan count of the envelope is refused as a conflict and nothing is stored.</summary>
    [Fact]
    public async Task A_plan_beyond_the_plan_count_of_the_envelope_is_refused_and_nothing_is_stored()
    {
        _scenario.AddPlan("First", _scenario.ResellerId);
        _scenario.AddPlan("Second", _scenario.ResellerId);

        var result = await _scenario.CreateAsReseller().HandleAsync(
            ResellerPlanScenario.Create("Third"), CancellationToken.None);

        Assert.Equal("ResellerPlanLimitReached", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(2, await _scenario.Seed.Plans.CountAsync());
        var entry = Assert.Single(_scenario.Audit.Entries);
        Assert.False(entry.Succeeded);
        Assert.Equal(AccountAuditActions.PlanCreated, entry.Action);
    }

    /// <summary>The plans another reseller owns and the administrator's do not count against the envelope.</summary>
    [Fact]
    public async Task Plans_other_owners_hold_do_not_count_against_the_envelope()
    {
        var rival = _scenario.AddRival();
        _scenario.AddPlan("RivalA", rival);
        _scenario.AddPlan("RivalB", rival);
        _scenario.AddPlan("AdminPlan", null);

        var result = await _scenario.CreateAsReseller().HandleAsync(
            ResellerPlanScenario.Create(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>A save that loses the race for the last slot is answered as the plan limit and stores nothing.</summary>
    [Fact]
    public async Task A_save_that_loses_the_race_for_the_last_slot_is_answered_as_the_plan_limit()
    {
        _scenario.SaveFailure.Next = new DbUpdateConcurrencyException("claimed elsewhere");

        var result = await _scenario.CreateAsReseller().HandleAsync(
            ResellerPlanScenario.Create(), CancellationToken.None);

        Assert.Equal("ResellerPlanLimitReached", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(await _scenario.Seed.Plans.ToListAsync());
        Assert.False(Assert.Single(_scenario.Audit.Entries).Succeeded);
    }

    /// <summary>A reseller may use a name the administrator already uses, because the two are never offered to one buyer.</summary>
    [Fact]
    public async Task A_reseller_may_use_a_name_the_administrator_already_uses()
    {
        _scenario.AddPlan("Gold", null);

        var result = await _scenario.CreateAsReseller().HandleAsync(
            ResellerPlanScenario.Create("Gold"), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>A reseller cannot name two of its own plans alike, whatever the case.</summary>
    [Fact]
    public async Task A_reseller_cannot_name_two_of_its_own_plans_alike()
    {
        _scenario.AddPlan("Gold", _scenario.ResellerId);

        var result = await _scenario.CreateAsReseller().HandleAsync(
            ResellerPlanScenario.Create("GOLD"), CancellationToken.None);

        Assert.Equal("PlanNameTaken", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.False(Assert.Single(_scenario.Audit.Entries).Succeeded);
    }

    /// <summary>An administrator may use a name a reseller already uses.</summary>
    [Fact]
    public async Task An_administrator_may_use_a_name_a_reseller_already_uses()
    {
        _scenario.AddPlan("Gold", _scenario.ResellerId);

        var result = await _scenario.CreateAsAdministrator().HandleAsync(
            ResellerPlanScenario.Create("Gold"), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>A reseller that has no row is answered as not found, the same as one that never existed.</summary>
    [Fact]
    public async Task A_reseller_with_no_row_is_answered_as_not_found()
    {
        var stranger = FakeCurrentUser.Reseller(Guid.NewGuid());
        await using var context = AccountsTestContext.Create(stranger);
        var handler = PlanHandlerFactory.Create(context, stranger, new RecordingAuditWriter(), new StubDisplayNames());

        var result = await handler.HandleAsync(ResellerPlanScenario.Create(), CancellationToken.None);

        Assert.Equal("ResellerNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(await context.Plans.ToListAsync());
    }
}
