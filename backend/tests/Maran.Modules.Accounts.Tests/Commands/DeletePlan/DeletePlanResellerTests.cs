using Maran.Modules.Accounts.Commands.DeletePlan;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.DeletePlan;

/// <summary>
/// Behavioral contract of <see cref="DeletePlanCommandHandler"/> for a reseller: it deletes only its own
/// plans, a plan it does not own is exactly as missing as a random id, a plan accounts still use is
/// refused, and a suspended reseller cannot delete at all.
/// </summary>
public sealed class DeletePlanResellerTests : IDisposable
{
    /// <summary>The reseller, its rival and the contexts both are seen through.</summary>
    private readonly ResellerPlanScenario _scenario = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _scenario.Dispose();
    }

    /// <summary>Builds a deletion request for <paramref name="planId"/>.</summary>
    /// <param name="planId">The plan to delete.</param>
    private static DeletePlanCommand Command(Guid planId)
    {
        return new DeletePlanCommand(planId, "203.0.113.7", "unit-tests");
    }

    /// <summary>A reseller deletes its own plan and the deletion is journalled with the reseller as actor.</summary>
    [Fact]
    public async Task A_reseller_deletes_its_own_plan_and_the_deletion_is_journalled_with_the_reseller_as_actor()
    {
        var plan = _scenario.AddPlan("Gold", _scenario.ResellerId);

        var result = await _scenario.DeleteAsReseller().HandleAsync(Command(plan.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(await _scenario.ReadPlanAsync(plan.Id));
        var entry = Assert.Single(_scenario.Audit.Entries);
        Assert.Equal(AccountAuditActions.PlanDeleted, entry.Action);
        Assert.True(entry.Succeeded);
        Assert.Equal("reseller", entry.ActorUsername);
        Assert.Equal(((ResellerCaller)_scenario.ResellerUser.Caller).UserId, entry.ActorUserId);
    }

    /// <summary>A plan another reseller owns is not found identically to a random id, and survives.</summary>
    [Fact]
    public async Task A_plan_another_reseller_owns_is_not_found_identically_to_a_random_id_and_survives()
    {
        var rivalPlan = _scenario.AddPlan("Silver", _scenario.AddRival());
        var handler = _scenario.DeleteAsReseller();

        var foreign = await handler.HandleAsync(Command(rivalPlan.Id), CancellationToken.None);
        var missing = await handler.HandleAsync(Command(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal("PlanNotFound", foreign.Error!.Code);
        Assert.Equal(ErrorType.NotFound, foreign.Error.Type);
        Assert.Equal(missing.Error!.Code, foreign.Error.Code);
        Assert.Equal(missing.Error.Type, foreign.Error.Type);
        Assert.NotNull(await _scenario.ReadPlanAsync(rivalPlan.Id));
        Assert.All(_scenario.Audit.Entries, entry =>
        {
            Assert.False(entry.Succeeded);
        });
    }

    /// <summary>A plan the administrator owns is not found for a reseller and survives.</summary>
    [Fact]
    public async Task A_plan_the_administrator_owns_is_not_found_for_a_reseller_and_survives()
    {
        var adminPlan = _scenario.AddPlan("Shared", null);

        var result = await _scenario.DeleteAsReseller().HandleAsync(Command(adminPlan.Id), CancellationToken.None);

        Assert.Equal("PlanNotFound", result.Error!.Code);
        Assert.NotNull(await _scenario.ReadPlanAsync(adminPlan.Id));
    }

    /// <summary>A plan a reseller's account still uses is refused as in use and survives.</summary>
    [Fact]
    public async Task A_plan_an_account_still_uses_is_refused_as_in_use_and_survives()
    {
        var plan = _scenario.AddPlan("Gold", _scenario.ResellerId);
        _scenario.AddAccount("olive", plan, _scenario.ResellerId);

        var result = await _scenario.DeleteAsReseller().HandleAsync(Command(plan.Id), CancellationToken.None);

        Assert.Equal("PlanInUse", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.NotNull(await _scenario.ReadPlanAsync(plan.Id));
        Assert.False(Assert.Single(_scenario.Audit.Entries).Succeeded);
    }

    /// <summary>Deleting a plan claims nothing, because it only frees room.</summary>
    [Fact]
    public async Task Deleting_a_plan_claims_nothing_on_the_resellers_row()
    {
        var plan = _scenario.AddPlan("Gold", _scenario.ResellerId);

        await _scenario.DeleteAsReseller().HandleAsync(Command(plan.Id), CancellationToken.None);

        Assert.Equal(0, (await _scenario.ReadResellerAsync()).AllocationRevision);
        Assert.Equal(0, await _scenario.Seed.Plans.CountAsync());
    }
}
