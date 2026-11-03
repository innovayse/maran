using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;

namespace Maran.Modules.Accounts.Tests.Services;

/// <summary>
/// The server-wide namespace of account names, reseller names and primary domains: every tenant's
/// claim is seen whoever asks, and a name nobody holds is free.
/// </summary>
public sealed class AccountNamespaceClaimsTests : IDisposable
{
    /// <summary>The world of two resellers, each with an account.</summary>
    private readonly ResellerAccountsWorld _world = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _world.Dispose();
    }

    /// <summary>Builds the claims reader over a context filtered for <paramref name="caller"/>.</summary>
    /// <param name="caller">The caller asking.</param>
    /// <returns>The reader.</returns>
    private AccountNamespaceClaims ClaimsFor(FakeCurrentUser caller)
    {
        return new AccountNamespaceClaims(_world.ContextFor(caller));
    }

    /// <summary>An administrator finds the name of a reseller's account taken (positive control).</summary>
    [Fact]
    public async Task An_administrator_finds_the_name_of_a_resellers_account_taken()
    {
        var taken = await ClaimsFor(FakeCurrentUser.Admin()).IsNameTakenAsync("bravo-one", CancellationToken.None);

        Assert.True(taken);
    }

    /// <summary>A reseller finds the name of another reseller's account taken although its own context hides that account.</summary>
    [Fact]
    public async Task A_reseller_finds_the_name_of_another_resellers_account_taken_though_it_cannot_see_the_account()
    {
        var context = _world.ContextFor(_world.UserA);
        Assert.Empty(context.Accounts.Where(account => account.Name == "bravo-one"));

        var taken = await new AccountNamespaceClaims(context).IsNameTakenAsync("bravo-one", CancellationToken.None);

        Assert.True(taken);
    }

    /// <summary>A name that a reseller holds is taken, because it is also the reseller's login name.</summary>
    [Fact]
    public async Task A_name_a_reseller_holds_is_taken()
    {
        var own = await ClaimsFor(_world.UserA).IsNameTakenAsync("alpha", CancellationToken.None);
        var foreign = await ClaimsFor(_world.UserA).IsNameTakenAsync("bravo", CancellationToken.None);

        Assert.True(own);
        Assert.True(foreign);
    }

    /// <summary>A name nobody holds is free for every kind of caller (refusing control).</summary>
    [Fact]
    public async Task A_name_nobody_holds_is_free_for_every_kind_of_caller()
    {
        Assert.False(await ClaimsFor(FakeCurrentUser.Admin()).IsNameTakenAsync("nobody", CancellationToken.None));
        Assert.False(await ClaimsFor(_world.UserA).IsNameTakenAsync("nobody", CancellationToken.None));
    }

    /// <summary>A name is compared exactly, so a longer name that merely starts with a held one is free.</summary>
    [Fact]
    public async Task A_name_that_only_starts_with_a_held_name_is_free()
    {
        var taken = await ClaimsFor(_world.UserA).IsNameTakenAsync("bravo-one-extra", CancellationToken.None);

        Assert.False(taken);
    }

    /// <summary>A reseller finds a domain another reseller's account holds taken, and an unused domain free.</summary>
    [Fact]
    public async Task A_reseller_finds_a_domain_another_resellers_account_holds_taken_and_an_unused_one_free()
    {
        var claims = ClaimsFor(_world.UserA);

        Assert.True(await claims.IsDomainTakenAsync("bravo-one.example.com", CancellationToken.None));
        Assert.True(await claims.IsDomainTakenAsync("alpha-one.example.com", CancellationToken.None));
        Assert.False(await claims.IsDomainTakenAsync("unused.example.com", CancellationToken.None));
    }
}
