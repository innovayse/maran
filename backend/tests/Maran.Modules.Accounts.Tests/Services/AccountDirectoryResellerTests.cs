using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Accounts.Tests.Services;

/// <summary>
/// Behavioral contract of <see cref="AccountDirectory"/> for a reseller caller: it reaches the accounts
/// it owns and no others, and an account it does not own answers exactly as one that does not exist,
/// so the window other modules and view-as rest on discloses nothing about another tenant.
/// </summary>
/// <remarks>
/// UNOBSERVED HERE: <see cref="AccountDirectory.FindIdsByNameAsync"/>, an <c>ILIKE</c> that only
/// PostgreSQL translates; the reseller arm of its scope is read from the same owner predicate the
/// reads below observe.
/// </remarks>
public sealed class AccountDirectoryResellerTests
{
    /// <summary>A reseller finds an account it owns: the positive control of the narrowing.</summary>
    [Fact]
    public async Task A_reseller_finds_an_account_it_owns()
    {
        var world = await TwoResellerWorld.SeedAsync();

        var snapshot = await FindAsync(world, world.AsAlpha(), world.AlphaAccount.Id);

        Assert.NotNull(snapshot);
        Assert.Equal("alphaacct", snapshot.Username);
    }

    /// <summary>
    /// A reseller finds nothing for an account another reseller owns, nor for the administrator's, nor
    /// for a random id: all three answer null, indistinguishably.
    /// </summary>
    [Fact]
    public async Task A_reseller_finds_nothing_for_a_foreign_account_exactly_as_for_a_random_id()
    {
        var world = await TwoResellerWorld.SeedAsync();

        Assert.Null(await FindAsync(world, world.AsAlpha(), world.BetaAccount.Id));
        Assert.Null(await FindAsync(world, world.AsAlpha(), world.AdminAccount.Id));
        Assert.Null(await FindAsync(world, world.AsAlpha(), Guid.NewGuid()));
    }

    /// <summary>An administrator finds every account, a reseller's included.</summary>
    [Fact]
    public async Task An_administrator_finds_every_account_including_a_resellers()
    {
        var world = await TwoResellerWorld.SeedAsync();

        Assert.NotNull(await FindAsync(world, FakeCurrentUser.Admin(), world.AlphaAccount.Id));
        Assert.NotNull(await FindAsync(world, FakeCurrentUser.Admin(), world.BetaAccount.Id));
        Assert.NotNull(await FindAsync(world, FakeCurrentUser.Admin(), world.AdminAccount.Id));
    }

    /// <summary>A customer is unchanged: it finds its own account and no other, owned by a reseller or not.</summary>
    [Fact]
    public async Task A_customer_is_unchanged_and_finds_only_its_own_account()
    {
        var world = await TwoResellerWorld.SeedAsync();
        var customer = FakeCurrentUser.Customer(world.AlphaAccount.Id);

        Assert.NotNull(await FindAsync(world, customer, world.AlphaAccount.Id));
        Assert.Null(await FindAsync(world, customer, world.BetaAccount.Id));
        Assert.Null(await FindAsync(world, customer, world.AdminAccount.Id));
    }

    /// <summary>An anonymous caller and a customer acting for no account find nothing.</summary>
    [Fact]
    public async Task An_anonymous_caller_and_an_accountless_customer_find_nothing()
    {
        var world = await TwoResellerWorld.SeedAsync();

        Assert.Null(await FindAsync(world, FakeCurrentUser.Anonymous(), world.AlphaAccount.Id));
        Assert.Null(await FindAsync(world, FakeCurrentUser.AccountlessCustomer(), world.AlphaAccount.Id));
    }

    /// <summary>The batched name lookup names only the reseller's own accounts, whatever ids are asked for.</summary>
    [Fact]
    public async Task The_name_lookup_names_only_the_resellers_own_accounts()
    {
        var world = await TwoResellerWorld.SeedAsync();
        var caller = world.AsAlpha();
        await using var dbContext = world.ContextFor(caller);

        var names = await new AccountDirectory(dbContext, caller).NamesOfAsync(
            [world.AlphaAccount.Id, world.BetaAccount.Id, world.AdminAccount.Id], CancellationToken.None);

        var only = Assert.Single(names);
        Assert.Equal(world.AlphaAccount.Id, only.Key);
        Assert.Equal("alphaacct", only.Value);
    }

    /// <summary>The same ids asked by an administrator are all named: the inverse control of the name lookup.</summary>
    [Fact]
    public async Task The_same_ids_asked_by_an_administrator_are_all_named()
    {
        var world = await TwoResellerWorld.SeedAsync();
        var caller = FakeCurrentUser.Admin();
        await using var dbContext = world.ContextFor(caller);

        var names = await new AccountDirectory(dbContext, caller).NamesOfAsync(
            [world.AlphaAccount.Id, world.BetaAccount.Id, world.AdminAccount.Id], CancellationToken.None);

        Assert.Equal(3, names.Count);
    }

    /// <summary>Reads one account through a directory built for the caller over a context filtered for it.</summary>
    /// <param name="world">The seeded world.</param>
    /// <param name="caller">The caller the context and the directory are built for.</param>
    /// <param name="accountId">The account asked about.</param>
    /// <returns>The snapshot, or <c>null</c> when the caller's scope holds no such account.</returns>
    private static async Task<AccountSnapshot?> FindAsync(TwoResellerWorld world, ICurrentUser caller, Guid accountId)
    {
        await using var dbContext = world.ContextFor(caller);
        return await new AccountDirectory(dbContext, caller).FindAsync(accountId, CancellationToken.None);
    }
}
