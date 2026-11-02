using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Tests.Domain;

/// <summary>Behavioural contract of the member half of <see cref="AccountMembership"/>.</summary>
public sealed class AccountMembershipGrantsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private static AccountMembership Member(AccountFeature grants)
    {
        return AccountMembership.Admit(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), grants, Now);
    }

    /// <summary>An admitted membership is a member's and holds exactly the grants it was given.</summary>
    [Fact]
    public void Admitting_a_login_creates_a_member_holding_the_given_grants()
    {
        var membership = Member(AccountFeature.Databases | AccountFeature.Backups);

        Assert.Equal(MembershipRole.Member, membership.Role);
        Assert.Equal(AccountFeature.Databases | AccountFeature.Backups, membership.Grants);
    }

    /// <summary>A member may use a granted feature and nothing else.</summary>
    [Fact]
    public void A_member_is_allowed_only_the_granted_features()
    {
        var membership = Member(AccountFeature.Databases);

        Assert.True(membership.Allows(AccountFeature.Databases));
        Assert.False(membership.Allows(AccountFeature.Mail));
        Assert.False(membership.Allows(AccountFeature.Backups));
    }

    /// <summary>An owner is not restricted by a grant set.</summary>
    /// <remarks>The inverse control: the owner is allowed what the member above was refused.</remarks>
    [Fact]
    public void An_owner_is_allowed_every_feature()
    {
        var owner = AccountMembership.Own(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);

        Assert.True(owner.Allows(AccountFeature.Mail));
        Assert.True(owner.Allows(AccountFeature.Backups));
    }

    /// <summary>Changing a member's grants replaces the whole set.</summary>
    [Fact]
    public void Changing_grants_replaces_the_whole_set()
    {
        var membership = Member(AccountFeature.Databases | AccountFeature.Cron);

        membership.ChangeGrants(AccountFeature.Backups);

        Assert.Equal(AccountFeature.Backups, membership.Grants);
        Assert.False(membership.Allows(AccountFeature.Databases));
    }

    /// <summary>Changing an owner's grants is a programming error, not a quiet no-op.</summary>
    [Fact]
    public void Changing_an_owners_grants_is_refused()
    {
        var owner = AccountMembership.Own(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);

        Assert.Throws<InvalidOperationException>(() =>
        {
            owner.ChangeGrants(AccountFeature.Mail);
        });
    }
}
