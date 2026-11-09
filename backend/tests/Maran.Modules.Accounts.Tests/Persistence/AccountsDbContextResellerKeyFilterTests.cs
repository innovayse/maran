using Maran.Modules.Accounts.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Persistence;

/// <summary>
/// The narrowing of an API key by the owner it carries (issue #368): a key a reseller owns is narrowed to
/// that reseller's accounts and plans exactly as the reseller's own login is, and a key the administrator
/// owns is narrowed to nothing. The reseller's login is the accepting control beside every refusal, so a
/// filter that hid everything from a key, or nothing, would fail one of them.
/// </summary>
public sealed class AccountsDbContextResellerKeyFilterTests
{
    /// <summary>A key a reseller owns sees the accounts and plans that reseller owns, and the same ones its login sees.</summary>
    [Fact]
    public async Task A_key_a_reseller_owns_sees_that_resellers_accounts_and_plans()
    {
        var world = await TwoResellerWorld.SeedAsync();
        await using var key = world.ContextFor(FakeCurrentUser.ApiKey(world.Alpha.Id));
        await using var login = world.ContextFor(world.AsAlpha());

        Assert.Equal("alphaacct", Assert.Single(await key.Accounts.Select(account => account.Name).ToListAsync()));
        Assert.Equal(world.AlphaPlan.Id, Assert.Single(await key.Plans.Select(plan => plan.Id).ToListAsync()));
        Assert.Equal(
            await login.Accounts.Select(account => account.Id).ToListAsync(),
            await key.Accounts.Select(account => account.Id).ToListAsync());
    }

    /// <summary>A key a reseller owns cannot see another reseller's or the administrator's rows, however it asks.</summary>
    [Fact]
    public async Task A_key_a_reseller_owns_cannot_see_another_resellers_or_the_administrators_rows()
    {
        var world = await TwoResellerWorld.SeedAsync();
        await using var key = world.ContextFor(FakeCurrentUser.ApiKey(world.Alpha.Id));

        Assert.Null(await key.Accounts.FirstOrDefaultAsync(account => account.Id == world.BetaAccount.Id));
        Assert.Null(await key.Accounts.FirstOrDefaultAsync(account => account.Id == world.AdminAccount.Id));
        Assert.Null(await key.Plans.FirstOrDefaultAsync(plan => plan.Id == world.BetaPlan.Id));
        Assert.Null(await key.Plans.FirstOrDefaultAsync(plan => plan.Id == world.AdminPlan.Id));
        Assert.Null(await key.Resellers.FirstOrDefaultAsync(reseller => reseller.Id == world.Beta.Id));
    }

    /// <summary>Two keys of two resellers on one database see disjoint rows.</summary>
    [Fact]
    public async Task Keys_of_two_resellers_see_disjoint_rows()
    {
        var world = await TwoResellerWorld.SeedAsync();
        await using var alpha = world.ContextFor(FakeCurrentUser.ApiKey(world.Alpha.Id));
        await using var beta = world.ContextFor(FakeCurrentUser.ApiKey(world.Beta.Id));

        Assert.Equal("alphaacct", Assert.Single(await alpha.Accounts.Select(account => account.Name).ToListAsync()));
        Assert.Equal("betaacct", Assert.Single(await beta.Accounts.Select(account => account.Name).ToListAsync()));
    }

    /// <summary>A key the administrator owns sees no account, plan or reseller: it is not an administrator.</summary>
    [Fact]
    public async Task A_key_the_administrator_owns_sees_nothing()
    {
        var world = await TwoResellerWorld.SeedAsync();
        await using var key = world.ContextFor(FakeCurrentUser.ApiKey(null));
        await using var administrator = world.ContextFor(FakeCurrentUser.Admin());

        Assert.Equal(3, await administrator.Accounts.CountAsync());
        Assert.Empty(await key.Accounts.ToListAsync());
        Assert.Empty(await key.Plans.ToListAsync());
        Assert.Empty(await key.Resellers.ToListAsync());
    }

    /// <summary>A key owned by a reseller that does not exist sees nothing, and not the rows that have no owner.</summary>
    [Fact]
    public async Task A_key_owned_by_a_reseller_that_owns_nothing_sees_nothing()
    {
        var world = await TwoResellerWorld.SeedAsync();
        await using var key = world.ContextFor(FakeCurrentUser.ApiKey(Guid.NewGuid()));

        Assert.Empty(await key.Accounts.ToListAsync());
        Assert.Empty(await key.Plans.ToListAsync());
    }
}
