using Maran.Modules.Accounts.Queries.ListAccountChoices;
using Maran.Modules.Accounts.Tests.TestSupport;

namespace Maran.Modules.Accounts.Tests.Queries.ListAccountChoices;

/// <summary>
/// Behavioral contract of <see cref="ListAccountChoicesQueryHandler"/> for a reseller caller: the
/// picker offers the reseller's own accounts and no others, because the context's filter narrows it.
/// </summary>
/// <remarks>
/// UNOBSERVED HERE: the search, an <c>ILIKE</c> only PostgreSQL translates; the page is read with no
/// search term.
/// </remarks>
public sealed class ListAccountChoicesAsResellerTests
{
    /// <summary>A reseller is offered only the accounts it owns.</summary>
    [Fact]
    public async Task A_reseller_is_offered_only_the_accounts_it_owns()
    {
        var world = await TwoResellerWorld.SeedAsync();
        await using var dbContext = world.ContextFor(world.AsAlpha());

        var result = await new ListAccountChoicesQueryHandler(dbContext)
            .HandleAsync(new ListAccountChoicesQuery(), CancellationToken.None);

        var choice = Assert.Single(result.Value.Items);
        Assert.Equal(world.AlphaAccount.Id, choice.Id);
        Assert.Equal(1, result.Value.Total);
    }

    /// <summary>An administrator is offered every account: the inverse control of the filter.</summary>
    [Fact]
    public async Task An_administrator_is_offered_every_account()
    {
        var world = await TwoResellerWorld.SeedAsync();
        await using var dbContext = world.ContextFor(FakeCurrentUser.Admin());

        var result = await new ListAccountChoicesQueryHandler(dbContext)
            .HandleAsync(new ListAccountChoicesQuery(), CancellationToken.None);

        Assert.Equal(3, result.Value.Total);
    }
}
