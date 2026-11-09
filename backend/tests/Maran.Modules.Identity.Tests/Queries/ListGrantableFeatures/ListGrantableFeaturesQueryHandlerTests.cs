using Maran.Modules.Identity.Queries.ListGrantableFeatures;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Tests.Queries.ListGrantableFeatures;

/// <summary>Behavioural contract of the grantable-features handler.</summary>
public sealed class ListGrantableFeaturesQueryHandlerTests : IDisposable
{
    private readonly SubUserWorld _world = new();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _world.Dispose();
    }

    private ListGrantableFeaturesQueryHandler NewHandler(StubCustomerUser? caller = null)
    {
        return new ListGrantableFeaturesQueryHandler(_world.Guard(caller), IdentityTestContext.DisplayNameResources());
    }

    /// <summary>The owner is offered every feature, each with a real label rather than its resource key.</summary>
    [Fact]
    public async Task The_owner_is_offered_every_feature_with_a_label()
    {
        var result = await NewHandler().HandleAsync(new ListGrantableFeaturesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [
                AccountFeature.Mail,
                AccountFeature.Dns,
                AccountFeature.Databases,
                AccountFeature.Cron,
                AccountFeature.FileTransfer,
                AccountFeature.Backups,
                AccountFeature.Sites,
                AccountFeature.Ssl,
                AccountFeature.Tasks,
                AccountFeature.Php,
                AccountFeature.Files,
            ],
            result.Value.Select(feature => { return feature.Feature; }));
        Assert.All(result.Value, feature =>
        {
            Assert.False(string.IsNullOrWhiteSpace(feature.FeatureDisplayName));
            Assert.DoesNotContain("AccountFeature", feature.FeatureDisplayName, StringComparison.Ordinal);
        });
    }

    /// <summary>A team member is not offered the list, because the form it fills is not theirs to use.</summary>
    [Fact]
    public async Task A_member_is_refused()
    {
        var member = await _world.SeedMemberAsync("deploy", AccountFeature.All);
        var caller = new StubCustomerUser(member.Id, member.Username, SubUserWorld.AccountId);

        var result = await NewHandler(caller).HandleAsync(new ListGrantableFeaturesQuery(), CancellationToken.None);

        Assert.Equal("SubUsersOwnerOnly", result.Error!.Code);
    }
}
