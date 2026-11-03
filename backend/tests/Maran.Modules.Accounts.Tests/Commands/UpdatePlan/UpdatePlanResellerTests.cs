using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Accounts.Tests.Commands.UpdatePlan;

/// <summary>
/// Behavioral contract of <see cref="Maran.Modules.Accounts.Commands.UpdatePlan.UpdatePlanCommandHandler"/>
/// for a reseller: it edits only the plans it owns, a plan it does not own is exactly as missing as a
/// random id, and a suspended reseller cannot edit at all.
/// </summary>
public sealed class UpdatePlanResellerTests : IDisposable
{
    /// <summary>The reseller, its rival and the contexts both are seen through.</summary>
    private readonly ResellerPlanScenario _scenario = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _scenario.Dispose();
    }

    /// <summary>A reseller's edit of its own plan is stored, answered with the plan and journalled with the reseller as actor.</summary>
    [Fact]
    public async Task A_resellers_edit_of_its_own_plan_is_stored_and_journalled_with_the_reseller_as_actor()
    {
        var plan = _scenario.AddPlan("Gold", _scenario.ResellerId);

        var result = await _scenario.UpdateAsReseller().HandleAsync(
            ResellerPlanScenario.Same(plan) with { MaxSites = 4 }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(_scenario.ResellerId, result.Value.ResellerId);
        Assert.Equal("acme", result.Value.ResellerName);
        Assert.Equal(4, (await _scenario.ReadPlanAsync(plan.Id))!.MaxSites);
        var entry = Assert.Single(_scenario.Audit.Entries);
        Assert.Equal(AccountAuditActions.PlanUpdated, entry.Action);
        Assert.True(entry.Succeeded);
        Assert.Equal("reseller", entry.ActorUsername);
        Assert.Equal(((ResellerCaller)_scenario.ResellerUser.Caller).UserId, entry.ActorUserId);
    }

    /// <summary>The edit never changes the plan's owner.</summary>
    [Fact]
    public async Task The_edit_never_changes_the_plans_owner()
    {
        var plan = _scenario.AddPlan("Gold", _scenario.ResellerId);

        await _scenario.UpdateAsReseller().HandleAsync(
            ResellerPlanScenario.Same(plan) with { Name = "Gold Plus" }, CancellationToken.None);

        Assert.Equal(_scenario.ResellerId, (await _scenario.ReadPlanAsync(plan.Id))!.ResellerId);
    }

    /// <summary>A plan another reseller owns is not found, identically to a random id, and is left unchanged.</summary>
    [Fact]
    public async Task A_plan_another_reseller_owns_is_not_found_identically_to_a_random_id()
    {
        var rivalPlan = _scenario.AddPlan("Silver", _scenario.AddRival());
        var handler = _scenario.UpdateAsReseller();

        var foreign = await handler.HandleAsync(
            ResellerPlanScenario.Same(rivalPlan) with { MaxSites = 1 }, CancellationToken.None);
        var missing = await handler.HandleAsync(
            ResellerPlanScenario.Same(rivalPlan) with { PlanId = Guid.NewGuid(), MaxSites = 1 }, CancellationToken.None);

        Assert.Equal("PlanNotFound", foreign.Error!.Code);
        Assert.Equal(ErrorType.NotFound, foreign.Error.Type);
        Assert.Equal(missing.Error!.Code, foreign.Error.Code);
        Assert.Equal(missing.Error.Type, foreign.Error.Type);
        Assert.Equal(5, (await _scenario.ReadPlanAsync(rivalPlan.Id))!.MaxSites);
        Assert.All(_scenario.Audit.Entries, entry =>
        {
            Assert.False(entry.Succeeded);
        });
    }

    /// <summary>A plan the administrator owns is not found for a reseller.</summary>
    [Fact]
    public async Task A_plan_the_administrator_owns_is_not_found_for_a_reseller()
    {
        var adminPlan = _scenario.AddPlan("Shared", null);

        var result = await _scenario.UpdateAsReseller().HandleAsync(
            ResellerPlanScenario.Same(adminPlan) with { MaxSites = 1 }, CancellationToken.None);

        Assert.Equal("PlanNotFound", result.Error!.Code);
        Assert.Equal(5, (await _scenario.ReadPlanAsync(adminPlan.Id))!.MaxSites);
    }

    /// <summary>An administrator can edit the reseller's plan, which is the inverse control for the refusals above.</summary>
    [Fact]
    public async Task An_administrator_can_edit_a_resellers_plan()
    {
        var plan = _scenario.AddPlan("Gold", _scenario.ResellerId);

        var result = await _scenario.UpdateAsAdministrator().HandleAsync(
            ResellerPlanScenario.Same(plan) with { MaxSites = 4 }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(_scenario.ResellerId, result.Value.ResellerId);
    }

    /// <summary>A reseller may rename a plan to a name the administrator uses.</summary>
    [Fact]
    public async Task A_reseller_may_rename_a_plan_to_a_name_the_administrator_uses()
    {
        _scenario.AddPlan("Gold", null);
        var plan = _scenario.AddPlan("Silver", _scenario.ResellerId);

        var result = await _scenario.UpdateAsReseller().HandleAsync(
            ResellerPlanScenario.Same(plan) with { Name = "Gold" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>A reseller cannot rename a plan to a name another of its plans holds.</summary>
    [Fact]
    public async Task A_reseller_cannot_rename_a_plan_to_a_name_another_of_its_plans_holds()
    {
        _scenario.AddPlan("Gold", _scenario.ResellerId);
        var plan = _scenario.AddPlan("Silver", _scenario.ResellerId);

        var result = await _scenario.UpdateAsReseller().HandleAsync(
            ResellerPlanScenario.Same(plan) with { Name = "gold" }, CancellationToken.None);

        Assert.Equal("PlanNameTaken", result.Error!.Code);
        Assert.Equal("Silver", (await _scenario.ReadPlanAsync(plan.Id))!.Name);
    }
}
