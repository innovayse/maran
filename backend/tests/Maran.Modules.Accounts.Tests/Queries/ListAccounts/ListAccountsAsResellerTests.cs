using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Queries.ListAccounts;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Accounts.Tests.Queries.ListAccounts;

/// <summary>
/// Behavioral contract of <see cref="ListAccountsQueryHandler"/> for a reseller caller: it lists the
/// accounts the reseller owns and no others, the administrator lists them all, and every row names
/// its owner. The handler adds no <c>Where</c> for the reseller: the context's query filter does it,
/// which is what these tests observe through a real context per caller.
/// </summary>
public sealed class ListAccountsAsResellerTests
{
    /// <summary>A reseller lists only the accounts it owns, and the total counts only those.</summary>
    [Fact]
    public async Task A_reseller_lists_only_the_accounts_it_owns()
    {
        var world = await TwoResellerWorld.SeedAsync();

        var result = await HandleAsync(world, world.AsAlpha(), new ListAccountsQuery());

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Total);
        var row = Assert.Single(result.Value.Items);
        Assert.Equal(world.AlphaAccount.Id, row.Id);
        Assert.Equal(world.Alpha.Id, row.ResellerId);
        Assert.Equal("alpha", row.ResellerName);
    }

    /// <summary>The other reseller reads its own account and not the first one's: the refusing control.</summary>
    [Fact]
    public async Task The_other_reseller_lists_its_own_account_and_never_the_first_ones()
    {
        var world = await TwoResellerWorld.SeedAsync();

        var result = await HandleAsync(world, world.AsBeta(), new ListAccountsQuery());

        var row = Assert.Single(result.Value.Items);
        Assert.Equal(world.BetaAccount.Id, row.Id);
        Assert.DoesNotContain(result.Value.Items, item =>
        {
            return item.Id == world.AlphaAccount.Id || item.Id == world.AdminAccount.Id;
        });
    }

    /// <summary>An administrator lists every account, each naming its owner.</summary>
    [Fact]
    public async Task An_administrator_lists_every_account_and_each_names_its_owner()
    {
        var world = await TwoResellerWorld.SeedAsync();

        var result = await HandleAsync(world, FakeCurrentUser.Admin(), new ListAccountsQuery());

        Assert.Equal(3, result.Value.Total);
        Assert.Equal("alpha", RowOf(result.Value, world.AlphaAccount.Id).ResellerName);
        Assert.Equal("beta", RowOf(result.Value, world.BetaAccount.Id).ResellerName);
        var own = RowOf(result.Value, world.AdminAccount.Id);
        Assert.Null(own.ResellerId);
        Assert.Null(own.ResellerName);
    }

    /// <summary>An administrator narrows the list to one reseller's accounts.</summary>
    [Fact]
    public async Task An_administrator_narrows_the_list_to_one_resellers_accounts()
    {
        var world = await TwoResellerWorld.SeedAsync();

        var result = await HandleAsync(
            world, FakeCurrentUser.Admin(), new ListAccountsQuery(ResellerId: world.Beta.Id));

        Assert.Equal(world.BetaAccount.Id, Assert.Single(result.Value.Items).Id);
        Assert.Equal(1, result.Value.Total);
    }

    /// <summary>
    /// A reseller naming another reseller's id gets an empty page, exactly as for an id that names no
    /// reseller: the filter, and not the query's own member, is what holds it to its rows.
    /// </summary>
    [Fact]
    public async Task A_reseller_naming_another_resellers_id_gets_an_empty_page_like_for_an_unknown_id()
    {
        var world = await TwoResellerWorld.SeedAsync();

        var foreign = await HandleAsync(world, world.AsAlpha(), new ListAccountsQuery(ResellerId: world.Beta.Id));
        var unknown = await HandleAsync(world, world.AsAlpha(), new ListAccountsQuery(ResellerId: Guid.NewGuid()));

        Assert.Empty(foreign.Value.Items);
        Assert.Equal(0, foreign.Value.Total);
        Assert.Empty(unknown.Value.Items);
        Assert.Equal(unknown.Value.Total, foreign.Value.Total);
    }

    /// <summary>The plan filter still works inside a reseller's own rows.</summary>
    [Fact]
    public async Task The_plan_filter_works_inside_a_resellers_own_rows_and_cannot_reach_a_foreign_plan()
    {
        var world = await TwoResellerWorld.SeedAsync();

        var own = await HandleAsync(world, world.AsAlpha(), new ListAccountsQuery(PlanId: world.AlphaPlan.Id));
        var foreign = await HandleAsync(world, world.AsAlpha(), new ListAccountsQuery(PlanId: world.BetaPlan.Id));

        Assert.Equal(world.AlphaAccount.Id, Assert.Single(own.Value.Items).Id);
        Assert.Empty(foreign.Value.Items);
    }

    /// <summary>Runs the handler over a context filtered for the caller.</summary>
    /// <param name="world">The seeded world.</param>
    /// <param name="caller">The caller the context is filtered for.</param>
    /// <param name="query">The list request.</param>
    /// <returns>The handler's answer.</returns>
    private static async Task<Result<PagedResult<AccountDto>>> HandleAsync(
        TwoResellerWorld world,
        ICurrentUser caller,
        ListAccountsQuery query)
    {
        await using var dbContext = world.ContextFor(caller);
        return await new ListAccountsQueryHandler(
                dbContext, new ResellerNameDirectory(dbContext), new StubDisplayNames())
            .HandleAsync(query, CancellationToken.None);
    }

    /// <summary>Picks one account's row out of a page.</summary>
    /// <param name="page">The page.</param>
    /// <param name="accountId">The account whose row is wanted.</param>
    /// <returns>The row.</returns>
    private static AccountDto RowOf(PagedResult<AccountDto> page, Guid accountId)
    {
        return page.Items.Single(item =>
        {
            return item.Id == accountId;
        });
    }
}
