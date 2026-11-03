using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Queries.ListPlans;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Accounts.Tests.Queries.ListPlans;

/// <summary>
/// Behavioral contract of <see cref="ListPlansQueryHandler"/> for a reseller caller: it lists the
/// plans the reseller owns and no others, the administrator lists them all, and each row names its
/// owner and counts only the accounts the caller may see.
/// </summary>
public sealed class ListPlansAsResellerTests
{
    /// <summary>A reseller lists only the plans it owns, with its own name on them.</summary>
    [Fact]
    public async Task A_reseller_lists_only_the_plans_it_owns()
    {
        var world = await TwoResellerWorld.SeedAsync();

        var result = await HandleAsync(world, world.AsAlpha(), new ListPlansQuery());

        var row = Assert.Single(result.Value.Items);
        Assert.Equal(world.AlphaPlan.Id, row.Id);
        Assert.Equal(world.Alpha.Id, row.ResellerId);
        Assert.Equal("alpha", row.ResellerName);
        Assert.Equal(1, row.AccountCount);
        Assert.Equal(1, result.Value.Total);
    }

    /// <summary>The other reseller never sees the first one's plan nor the administrator's.</summary>
    [Fact]
    public async Task The_other_reseller_never_sees_the_first_ones_plan_or_the_administrators()
    {
        var world = await TwoResellerWorld.SeedAsync();

        var result = await HandleAsync(world, world.AsBeta(), new ListPlansQuery());

        Assert.Equal(world.BetaPlan.Id, Assert.Single(result.Value.Items).Id);
    }

    /// <summary>An administrator lists every plan, each naming its owner, the administrator's own with none.</summary>
    [Fact]
    public async Task An_administrator_lists_every_plan_and_each_names_its_owner()
    {
        var world = await TwoResellerWorld.SeedAsync();

        var result = await HandleAsync(world, FakeCurrentUser.Admin(), new ListPlansQuery());

        Assert.Equal(3, result.Value.Total);
        Assert.Equal("alpha", RowOf(result.Value, world.AlphaPlan.Id).ResellerName);
        Assert.Equal("beta", RowOf(result.Value, world.BetaPlan.Id).ResellerName);
        Assert.Null(RowOf(result.Value, world.AdminPlan.Id).ResellerName);
        Assert.Null(RowOf(result.Value, world.AdminPlan.Id).ResellerId);
    }

    /// <summary>An administrator narrows the list to one reseller's plans.</summary>
    [Fact]
    public async Task An_administrator_narrows_the_list_to_one_resellers_plans()
    {
        var world = await TwoResellerWorld.SeedAsync();

        var result = await HandleAsync(world, FakeCurrentUser.Admin(), new ListPlansQuery(ResellerId: world.Beta.Id));

        Assert.Equal(world.BetaPlan.Id, Assert.Single(result.Value.Items).Id);
    }

    /// <summary>A reseller naming another reseller's id gets an empty page, like for an unknown id.</summary>
    [Fact]
    public async Task A_reseller_naming_another_resellers_id_gets_an_empty_page_like_for_an_unknown_id()
    {
        var world = await TwoResellerWorld.SeedAsync();

        var foreign = await HandleAsync(world, world.AsAlpha(), new ListPlansQuery(ResellerId: world.Beta.Id));
        var unknown = await HandleAsync(world, world.AsAlpha(), new ListPlansQuery(ResellerId: Guid.NewGuid()));

        Assert.Empty(foreign.Value.Items);
        Assert.Equal(unknown.Value.Total, foreign.Value.Total);
    }

    /// <summary>Runs the handler over a context filtered for the caller.</summary>
    /// <param name="world">The seeded world.</param>
    /// <param name="caller">The caller the context is filtered for.</param>
    /// <param name="query">The list request.</param>
    /// <returns>The handler's answer.</returns>
    private static async Task<Result<PagedResult<PlanDto>>> HandleAsync(
        TwoResellerWorld world,
        ICurrentUser caller,
        ListPlansQuery query)
    {
        await using var dbContext = world.ContextFor(caller);
        return await new ListPlansQueryHandler(dbContext, new ResellerNameDirectory(dbContext), new StubDisplayNames())
            .HandleAsync(query, CancellationToken.None);
    }

    /// <summary>Picks one plan's row out of a page.</summary>
    /// <param name="page">The page.</param>
    /// <param name="planId">The plan whose row is wanted.</param>
    /// <returns>The row.</returns>
    private static PlanDto RowOf(PagedResult<PlanDto> page, Guid planId)
    {
        return page.Items.Single(item =>
        {
            return item.Id == planId;
        });
    }
}
