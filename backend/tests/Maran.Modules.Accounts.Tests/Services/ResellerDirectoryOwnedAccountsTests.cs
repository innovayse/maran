using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;

namespace Maran.Modules.Accounts.Tests.Services;

/// <summary>
/// What <see cref="ResellerDirectory.ListOwnedAccountIdsAsync"/> answers per caller (issue #370): a
/// reseller its own accounts and no other's, and every other caller nothing — the answer another
/// module scopes the audit journal by.
/// </summary>
public sealed class ResellerDirectoryOwnedAccountsTests
{
    /// <summary>A reseller is told its own account ids and not the other reseller's or the administrator's.</summary>
    [Fact]
    public async Task A_reseller_is_told_exactly_the_accounts_it_owns()
    {
        var world = await TwoResellerWorld.SeedAsync();
        var alpha = world.AsAlpha();
        await using var dbContext = world.ContextFor(alpha);

        var ids = await new ResellerDirectory(dbContext, alpha).ListOwnedAccountIdsAsync(CancellationToken.None);

        Assert.Equal([world.AlphaAccount.Id], ids);
    }

    /// <summary>The inverse control: the other reseller over the same database is told only its own account.</summary>
    [Fact]
    public async Task The_other_reseller_over_the_same_database_is_told_only_its_own()
    {
        var world = await TwoResellerWorld.SeedAsync();
        var beta = world.AsBeta();
        await using var dbContext = world.ContextFor(beta);

        var ids = await new ResellerDirectory(dbContext, beta).ListOwnedAccountIdsAsync(CancellationToken.None);

        Assert.Equal([world.BetaAccount.Id], ids);
    }

    /// <summary>An administrator, a customer, an accountless customer and an anonymous caller are told none.</summary>
    [Fact]
    public async Task Every_caller_that_is_not_a_reseller_is_told_none()
    {
        var world = await TwoResellerWorld.SeedAsync();
        var callers = new[]
        {
            FakeCurrentUser.Admin(),
            FakeCurrentUser.Customer(world.AlphaAccount.Id),
            FakeCurrentUser.AccountlessCustomer(),
            FakeCurrentUser.Anonymous(),
        };

        foreach (var caller in callers)
        {
            await using var dbContext = world.ContextFor(caller);

            Assert.Empty(await new ResellerDirectory(dbContext, caller).ListOwnedAccountIdsAsync(CancellationToken.None));
        }
    }
}
