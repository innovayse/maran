using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Queries.GetPlan;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Accounts.Tests.Queries.GetPlan;

/// <summary>
/// Behavioral contract of <see cref="GetPlanQueryHandler"/> for a reseller caller: a plan it owns reads
/// with its owner's name, and any other plan answers the same <c>PlanNotFound</c> as one that does not exist.
/// </summary>
public sealed class GetPlanAsResellerTests
{
    /// <summary>A reseller reads a plan it owns, with its owner's name.</summary>
    [Fact]
    public async Task A_reseller_reads_a_plan_it_owns_with_its_owners_name()
    {
        var world = await TwoResellerWorld.SeedAsync();

        var result = await HandleAsync(world, world.AsAlpha(), world.AlphaPlan.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(world.Alpha.Id, result.Value.ResellerId);
        Assert.Equal("alpha", result.Value.ResellerName);
        Assert.Equal(1, result.Value.AccountCount);
    }

    /// <summary>A plan another reseller owns answers the same not found as a random id.</summary>
    [Fact]
    public async Task A_plan_another_reseller_owns_answers_the_same_not_found_as_a_random_id()
    {
        var world = await TwoResellerWorld.SeedAsync();

        var foreign = await HandleAsync(world, world.AsAlpha(), world.BetaPlan.Id);
        var unknown = await HandleAsync(world, world.AsAlpha(), Guid.NewGuid());

        Assert.Equal("PlanNotFound", foreign.Error!.Code);
        Assert.Equal(ErrorType.NotFound, foreign.Error.Type);
        Assert.Equal(unknown.Error!.Code, foreign.Error.Code);
        Assert.Equal(unknown.Error.Type, foreign.Error.Type);
    }

    /// <summary>A plan the administrator owns is not found for a reseller.</summary>
    [Fact]
    public async Task A_plan_the_administrator_owns_is_not_found_for_a_reseller()
    {
        var world = await TwoResellerWorld.SeedAsync();

        var result = await HandleAsync(world, world.AsAlpha(), world.AdminPlan.Id);

        Assert.Equal("PlanNotFound", result.Error!.Code);
    }

    /// <summary>An administrator reads any plan, the administrator's own with no owner name.</summary>
    [Fact]
    public async Task An_administrator_reads_any_plan_and_the_administrators_own_names_no_owner()
    {
        var world = await TwoResellerWorld.SeedAsync();

        var reseller = await HandleAsync(world, FakeCurrentUser.Admin(), world.BetaPlan.Id);
        var own = await HandleAsync(world, FakeCurrentUser.Admin(), world.AdminPlan.Id);

        Assert.Equal("beta", reseller.Value.ResellerName);
        Assert.Null(own.Value.ResellerName);
    }

    /// <summary>Runs the handler over a context filtered for the caller.</summary>
    /// <param name="world">The seeded world.</param>
    /// <param name="caller">The caller the context is filtered for.</param>
    /// <param name="planId">The plan to read.</param>
    /// <returns>The handler's answer.</returns>
    private static async Task<Result<PlanDto>> HandleAsync(TwoResellerWorld world, ICurrentUser caller, Guid planId)
    {
        await using var dbContext = world.ContextFor(caller);
        return await new GetPlanQueryHandler(dbContext, new ResellerNameDirectory(dbContext), new StubDisplayNames())
            .HandleAsync(new GetPlanQuery(planId), CancellationToken.None);
    }
}
