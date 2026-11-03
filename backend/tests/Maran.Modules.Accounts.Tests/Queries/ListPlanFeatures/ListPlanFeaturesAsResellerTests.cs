using Maran.Modules.Accounts.Queries.ListPlanFeatures;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Extensions;

namespace Maran.Modules.Accounts.Tests.Queries.ListPlanFeatures;

/// <summary>
/// Behavioral contract of <see cref="ListPlanFeaturesQueryHandler"/> for a reseller caller: it is
/// offered only the features of its own envelope, because a plan selling any other is refused.
/// </summary>
public sealed class ListPlanFeaturesAsResellerTests
{
    /// <summary>A reseller is offered only the features its envelope holds.</summary>
    [Fact]
    public async Task A_reseller_is_offered_only_the_features_its_envelope_holds()
    {
        var world = await TwoResellerWorld.SeedAsync();
        var narrow = ResellerTestData.Reseller(
            "narrow", ResellerTestData.Envelope(features: AccountFeature.Mail | AccountFeature.Dns));
        await using (var seeding = world.ContextFor(FakeCurrentUser.Admin()))
        {
            seeding.Resellers.Add(narrow);
            await seeding.SaveChangesAsync();
        }

        var caller = FakeCurrentUser.Reseller(narrow.Id);
        await using var dbContext = world.ContextFor(caller);
        var result = await new ListPlanFeaturesQueryHandler(
                dbContext, new ResellerScope(dbContext, caller), new StubDisplayNames())
            .HandleAsync(new ListPlanFeaturesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [AccountFeature.Mail, AccountFeature.Dns],
            result.Value!.Select(row =>
            {
                return row.Feature;
            }));
    }

    /// <summary>An administrator is offered every feature: the inverse control of the narrowing.</summary>
    [Fact]
    public async Task An_administrator_is_offered_every_feature()
    {
        var world = await TwoResellerWorld.SeedAsync();
        var caller = FakeCurrentUser.Admin();
        await using var dbContext = world.ContextFor(caller);

        var result = await new ListPlanFeaturesQueryHandler(
                dbContext, new ResellerScope(dbContext, caller), new StubDisplayNames())
            .HandleAsync(new ListPlanFeaturesQuery(), CancellationToken.None);

        Assert.Equal(
            AccountFeature.All.Members(),
            result.Value!.Select(row =>
            {
                return row.Feature;
            }));
    }

    /// <summary>A reseller whose row no longer exists is answered reseller not found.</summary>
    [Fact]
    public async Task A_reseller_whose_row_no_longer_exists_is_answered_reseller_not_found()
    {
        var world = await TwoResellerWorld.SeedAsync();
        var caller = FakeCurrentUser.Reseller(Guid.NewGuid());
        await using var dbContext = world.ContextFor(caller);

        var result = await new ListPlanFeaturesQueryHandler(
                dbContext, new ResellerScope(dbContext, caller), new StubDisplayNames())
            .HandleAsync(new ListPlanFeaturesQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("ResellerNotFound", result.Error!.Code);
    }
}
