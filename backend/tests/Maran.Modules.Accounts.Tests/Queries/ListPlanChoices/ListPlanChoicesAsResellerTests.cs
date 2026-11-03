using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Queries.ListPlanChoices;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Accounts.Tests.Queries.ListPlanChoices;

/// <summary>
/// Behavioral contract of <see cref="ListPlanChoicesQueryHandler"/> for the owner an account is sold
/// for: a reseller is offered its own plans only, an administrator the plans of the owner it names (its
/// own when it names none), and a reseller naming another owner is answered <c>ResellerNotFound</c>.
/// </summary>
public sealed class ListPlanChoicesAsResellerTests
{
    /// <summary>A reseller is offered its own plans and no others, whether or not it names itself.</summary>
    [Fact]
    public async Task A_reseller_is_offered_its_own_plans_and_no_others()
    {
        var world = await TwoResellerWorld.SeedAsync();

        var unnamed = await HandleAsync(world, world.AsAlpha(), new ListPlanChoicesQuery());
        var named = await HandleAsync(world, world.AsAlpha(), new ListPlanChoicesQuery(ForResellerId: world.Alpha.Id));

        Assert.Equal(world.AlphaPlan.Id, Assert.Single(unnamed.Value.Items).Id);
        Assert.Equal(world.AlphaPlan.Id, Assert.Single(named.Value.Items).Id);
    }

    /// <summary>A reseller naming another reseller is answered reseller not found.</summary>
    [Fact]
    public async Task A_reseller_naming_another_reseller_is_answered_reseller_not_found()
    {
        var world = await TwoResellerWorld.SeedAsync();

        var foreign = await HandleAsync(world, world.AsAlpha(), new ListPlanChoicesQuery(ForResellerId: world.Beta.Id));
        var unknown = await HandleAsync(
            world, world.AsAlpha(), new ListPlanChoicesQuery(ForResellerId: Guid.NewGuid()));

        Assert.False(foreign.IsSuccess);
        Assert.Equal("ResellerNotFound", foreign.Error!.Code);
        Assert.Equal(ErrorType.NotFound, foreign.Error.Type);
        Assert.Equal(unknown.Error!.Code, foreign.Error.Code);
    }

    /// <summary>An administrator naming no owner is offered the administrator's own plans only.</summary>
    [Fact]
    public async Task An_administrator_naming_no_owner_is_offered_its_own_plans_only()
    {
        var world = await TwoResellerWorld.SeedAsync();

        var result = await HandleAsync(world, FakeCurrentUser.Admin(), new ListPlanChoicesQuery());

        Assert.Equal(world.AdminPlan.Id, Assert.Single(result.Value.Items).Id);
    }

    /// <summary>An administrator naming a reseller is offered that reseller's plans.</summary>
    [Fact]
    public async Task An_administrator_naming_a_reseller_is_offered_that_resellers_plans()
    {
        var world = await TwoResellerWorld.SeedAsync();

        var result = await HandleAsync(
            world, FakeCurrentUser.Admin(), new ListPlanChoicesQuery(ForResellerId: world.Beta.Id));

        Assert.Equal(world.BetaPlan.Id, Assert.Single(result.Value.Items).Id);
    }

    /// <summary>Runs the handler over a context filtered for the caller.</summary>
    /// <param name="world">The seeded world.</param>
    /// <param name="caller">The caller the context and the scope are built for.</param>
    /// <param name="query">The picker request.</param>
    /// <returns>The handler's answer.</returns>
    private static async Task<Result<PagedResult<PlanChoiceDto>>> HandleAsync(
        TwoResellerWorld world,
        ICurrentUser caller,
        ListPlanChoicesQuery query)
    {
        await using var dbContext = world.ContextFor(caller);
        return await new ListPlanChoicesQueryHandler(
                dbContext, new ResellerScope(dbContext, caller), new StubDisplayNames())
            .HandleAsync(query, CancellationToken.None);
    }
}
