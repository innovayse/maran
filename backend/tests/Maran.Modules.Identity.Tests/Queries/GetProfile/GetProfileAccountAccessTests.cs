using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Queries.GetProfile;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Tests.Queries.GetProfile;

/// <summary>Behavioural contract of the account-access advice on the caller's own profile.</summary>
public sealed class GetProfileAccountAccessTests : IDisposable
{
    private readonly SubUserWorld _world = new();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _world.Dispose();
    }

    private GetProfileQueryHandler NewHandler()
    {
        return new GetProfileQueryHandler(_world.Context, _world.Clock, IdentityTestContext.DisplayNameResources());
    }

    /// <summary>An owner is told they own the account and may use every feature.</summary>
    [Fact]
    public async Task An_owner_is_told_they_may_use_every_feature()
    {
        var result = await NewHandler().HandleAsync(
            new GetProfileQuery(_world.Owner.Id, SubUserWorld.AccountId), CancellationToken.None);

        var access = result.Value.AccountAccess!;
        Assert.Equal(MembershipRole.Owner, access.Role);
        Assert.Equal("Owner", access.RoleDisplayName);
        Assert.Equal(AccountFeature.All, access.Features.Aggregate(AccountFeature.None, (set, feature) => { return set | feature; }));
    }

    /// <summary>A member is told exactly the features they were given.</summary>
    [Fact]
    public async Task A_member_is_told_only_the_features_they_were_given()
    {
        var member = await _world.SeedMemberAsync("deploy", AccountFeature.Cron | AccountFeature.Backups);

        var result = await NewHandler().HandleAsync(
            new GetProfileQuery(member.Id, SubUserWorld.AccountId), CancellationToken.None);

        var access = result.Value.AccountAccess!;
        Assert.Equal(MembershipRole.Member, access.Role);
        Assert.Equal("Team member", access.RoleDisplayName);
        Assert.Equal([AccountFeature.Cron, AccountFeature.Backups], access.Features);
    }

    /// <summary>A sign-in acting for no account carries no advice at all.</summary>
    [Fact]
    public async Task A_sign_in_acting_for_no_account_has_no_access_advice()
    {
        var result = await NewHandler().HandleAsync(new GetProfileQuery(_world.Owner.Id), CancellationToken.None);

        Assert.Null(result.Value.AccountAccess);
    }
}
