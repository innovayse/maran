using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Queries.GetAccountResourceSummary;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Accounts.Tests.Queries.GetAccountResourceSummary;

/// <summary>
/// Behavioral contract of the resource summary for a reseller caller: it is shown the plan's limits
/// and never a count, because no module that owns a resource shows a reseller any row and a count of
/// zero would be a lie; another reseller's account is not found before any counter is asked.
/// </summary>
public sealed class GetAccountResourceSummaryAsResellerTests
{
    /// <summary>A reseller's own account is summarised with every count unavailable beside its limit.</summary>
    [Fact]
    public async Task A_resellers_own_account_is_summarised_with_every_count_unavailable_beside_its_limit()
    {
        var world = await TwoResellerWorld.SeedAsync();
        var sites = new StubAccountResourceCounter(AccountResourceCount.Counted(AccountResourceKind.Sites, 4));

        var result = await HandleAsync(world, world.AsAlpha(), world.AlphaAccount.Id, sites);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value.Resources);
        Assert.Equal(AccountResourceKind.Sites, row.Kind);
        Assert.Equal(new UnavailableResourceCountDto(), row.Count);
        Assert.Equal(new LimitedResourceCeilingDto(5), row.Limit);
        Assert.Empty(sites.Asked);
    }

    /// <summary>The same account asked by an administrator is counted, the inverse control.</summary>
    [Fact]
    public async Task The_same_account_asked_by_an_administrator_is_counted()
    {
        var world = await TwoResellerWorld.SeedAsync();
        var sites = new StubAccountResourceCounter(AccountResourceCount.Counted(AccountResourceKind.Sites, 4));

        var result = await HandleAsync(world, FakeCurrentUser.Admin(), world.AlphaAccount.Id, sites);

        var row = Assert.Single(result.Value.Resources);
        Assert.Equal(new CountedResourceCountDto(4), row.Count);
        Assert.Equal([world.AlphaAccount.Id], sites.Asked);
    }

    /// <summary>Another reseller's account is not found, and no counter is asked about it.</summary>
    [Fact]
    public async Task Another_resellers_account_is_not_found_and_no_counter_is_asked()
    {
        var world = await TwoResellerWorld.SeedAsync();
        var sites = new StubAccountResourceCounter(AccountResourceCount.Counted(AccountResourceKind.Sites, 4));

        var foreign = await HandleAsync(world, world.AsAlpha(), world.BetaAccount.Id, sites);
        var unknown = await HandleAsync(world, world.AsAlpha(), Guid.NewGuid(), sites);

        Assert.Equal("AccountNotFound", foreign.Error!.Code);
        Assert.Equal(ErrorType.NotFound, foreign.Error.Type);
        Assert.Equal(unknown.Error!.Code, foreign.Error.Code);
        Assert.Empty(sites.Asked);
    }

    /// <summary>Runs the handler over a context filtered for the caller.</summary>
    /// <param name="world">The seeded world.</param>
    /// <param name="caller">The caller the context and the scope are built for.</param>
    /// <param name="accountId">The account to summarise.</param>
    /// <param name="counter">The one counter registered for this test.</param>
    /// <returns>The handler's answer.</returns>
    private static async Task<Result<AccountResourceSummaryDto>> HandleAsync(
        TwoResellerWorld world,
        ICurrentUser caller,
        Guid accountId,
        StubAccountResourceCounter counter)
    {
        await using var dbContext = world.ContextFor(caller);
        var reader = new AccountResourceSummaryReader(dbContext, new ResellerScope(dbContext, caller), [counter]);
        return await new GetAccountResourceSummaryQueryHandler(reader)
            .HandleAsync(new GetAccountResourceSummaryQuery(accountId), CancellationToken.None);
    }
}
