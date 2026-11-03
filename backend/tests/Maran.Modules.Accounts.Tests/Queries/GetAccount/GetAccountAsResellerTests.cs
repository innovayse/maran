using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Queries.GetAccount;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Accounts.Tests.Queries.GetAccount;

/// <summary>
/// Behavioral contract of <see cref="GetAccountQueryHandler"/> for a reseller caller: an account it
/// owns reads with its owner's name, and any other account — another reseller's, the
/// administrator's, one that does not exist — answers the same <c>AccountNotFound</c>.
/// </summary>
public sealed class GetAccountAsResellerTests
{
    /// <summary>A reseller reads an account it owns, with its owner's name.</summary>
    [Fact]
    public async Task A_reseller_reads_an_account_it_owns_with_its_owners_name()
    {
        var world = await TwoResellerWorld.SeedAsync();

        var result = await HandleAsync(world, world.AsAlpha(), world.AlphaAccount.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal("alphaacct", result.Value.Name);
        Assert.Equal(world.Alpha.Id, result.Value.ResellerId);
        Assert.Equal("alpha", result.Value.ResellerName);
        Assert.Equal("Alpha plan", result.Value.PlanDisplayName);
    }

    /// <summary>
    /// An account another reseller owns answers not found, identical to an id that exists nowhere, so
    /// the answer confirms nothing about the other tenant.
    /// </summary>
    [Fact]
    public async Task An_account_another_reseller_owns_answers_the_same_not_found_as_a_random_id()
    {
        var world = await TwoResellerWorld.SeedAsync();

        var foreign = await HandleAsync(world, world.AsAlpha(), world.BetaAccount.Id);
        var unknown = await HandleAsync(world, world.AsAlpha(), Guid.NewGuid());

        Assert.False(foreign.IsSuccess);
        Assert.Equal("AccountNotFound", foreign.Error!.Code);
        Assert.Equal(ErrorType.NotFound, foreign.Error.Type);
        Assert.Equal(unknown.Error!.Code, foreign.Error.Code);
        Assert.Equal(unknown.Error.Type, foreign.Error.Type);
    }

    /// <summary>An account the administrator owns is not found for a reseller.</summary>
    [Fact]
    public async Task An_account_the_administrator_owns_is_not_found_for_a_reseller()
    {
        var world = await TwoResellerWorld.SeedAsync();

        var result = await HandleAsync(world, world.AsAlpha(), world.AdminAccount.Id);

        Assert.Equal("AccountNotFound", result.Error!.Code);
    }

    /// <summary>An administrator reads any account, the administrator's own with no owner name.</summary>
    [Fact]
    public async Task An_administrator_reads_any_account_and_the_administrators_own_names_no_owner()
    {
        var world = await TwoResellerWorld.SeedAsync();

        var reseller = await HandleAsync(world, FakeCurrentUser.Admin(), world.BetaAccount.Id);
        var own = await HandleAsync(world, FakeCurrentUser.Admin(), world.AdminAccount.Id);

        Assert.Equal("beta", reseller.Value.ResellerName);
        Assert.Null(own.Value.ResellerId);
        Assert.Null(own.Value.ResellerName);
    }

    /// <summary>Runs the handler over a context filtered for the caller.</summary>
    /// <param name="world">The seeded world.</param>
    /// <param name="caller">The caller the context is filtered for.</param>
    /// <param name="accountId">The account to read.</param>
    /// <returns>The handler's answer.</returns>
    private static async Task<Result<AccountDetailDto>> HandleAsync(
        TwoResellerWorld world,
        ICurrentUser caller,
        Guid accountId)
    {
        await using var dbContext = world.ContextFor(caller);
        return await new GetAccountQueryHandler(
                dbContext, new ResellerNameDirectory(dbContext), new StubDisplayNames())
            .HandleAsync(new GetAccountQuery(accountId), CancellationToken.None);
    }
}
