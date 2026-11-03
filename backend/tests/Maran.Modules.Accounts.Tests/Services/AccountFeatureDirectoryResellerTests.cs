using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Accounts.Tests.Services;

/// <summary>
/// Behavioral contract of <see cref="AccountFeatureDirectory"/> for a reseller caller: it is always
/// answered no, because it acts for no account and is sold no plan; the feature of a customer's plan
/// is reached by a reseller only through a view-as session, which speaks as that customer.
/// </summary>
public sealed class AccountFeatureDirectoryResellerTests
{
    /// <summary>A reseller is told no for its own account even when the account's plan includes the feature.</summary>
    [Fact]
    public async Task A_reseller_is_told_no_for_its_own_account_even_when_the_plan_includes_the_feature()
    {
        var world = await TwoResellerWorld.SeedAsync();
        var caller = world.AsAlpha();
        await using var dbContext = world.ContextFor(caller);

        var answer = await new AccountFeatureDirectory(dbContext, caller)
            .IncludesAsync(world.AlphaAccount.Id, AccountFeature.Databases, CancellationToken.None);

        Assert.False(answer);
    }

    /// <summary>The same question asked by an administrator is answered yes, the inverse control.</summary>
    [Fact]
    public async Task The_same_question_asked_by_an_administrator_is_answered_yes()
    {
        var world = await TwoResellerWorld.SeedAsync();
        var caller = FakeCurrentUser.Admin();
        await using var dbContext = world.ContextFor(caller);

        var answer = await new AccountFeatureDirectory(dbContext, caller)
            .IncludesAsync(world.AlphaAccount.Id, AccountFeature.Databases, CancellationToken.None);

        Assert.True(answer);
    }

    /// <summary>A reseller is told no for another reseller's account and for an unknown one alike.</summary>
    [Fact]
    public async Task A_reseller_is_told_no_for_a_foreign_and_for_an_unknown_account_alike()
    {
        var world = await TwoResellerWorld.SeedAsync();
        var caller = world.AsAlpha();
        await using var dbContext = world.ContextFor(caller);
        var directory = new AccountFeatureDirectory(dbContext, caller);

        var feature = AccountFeature.Databases;

        Assert.False(await directory.IncludesAsync(world.BetaAccount.Id, feature, CancellationToken.None));
        Assert.False(await directory.IncludesAsync(Guid.NewGuid(), feature, CancellationToken.None));
    }
}
