using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>Behavioural contract of the Sdk window that tells the plan filter what a person was given.</summary>
public sealed class AccountMemberGrantsTests : IDisposable
{
    private readonly SubUserWorld _world = new();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _world.Dispose();
    }

    private AccountMemberGrants Window()
    {
        return new AccountMemberGrants(_world.Context);
    }

    /// <summary>The owner may use every feature.</summary>
    [Fact]
    public async Task The_owner_is_permitted_every_feature()
    {
        Assert.True(await Window().PermitsAsync(
            _world.Owner.Id, SubUserWorld.AccountId, AccountFeature.Mail, CancellationToken.None));
    }

    /// <summary>A member may use what was granted and is refused what was not.</summary>
    [Fact]
    public async Task A_member_is_permitted_only_what_was_granted()
    {
        var member = await _world.SeedMemberAsync("deploy", AccountFeature.Databases);

        Assert.True(await Window().PermitsAsync(
            member.Id, SubUserWorld.AccountId, AccountFeature.Databases, CancellationToken.None));
        Assert.False(await Window().PermitsAsync(
            member.Id, SubUserWorld.AccountId, AccountFeature.Backups, CancellationToken.None));
    }

    /// <summary>A person with no membership at all is refused, so a removed member loses access at once.</summary>
    [Fact]
    public async Task A_person_with_no_membership_is_refused()
    {
        Assert.False(await Window().PermitsAsync(
            Guid.NewGuid(), SubUserWorld.AccountId, AccountFeature.Databases, CancellationToken.None));
    }

    /// <summary>A membership of one account says nothing about another.</summary>
    [Fact]
    public async Task A_membership_of_another_account_grants_nothing_here()
    {
        var elsewhere = Guid.NewGuid();
        var member = await _world.SeedMemberAsync("deploy", AccountFeature.All, elsewhere);

        Assert.False(await Window().PermitsAsync(
            member.Id, SubUserWorld.AccountId, AccountFeature.Databases, CancellationToken.None));
    }
}
