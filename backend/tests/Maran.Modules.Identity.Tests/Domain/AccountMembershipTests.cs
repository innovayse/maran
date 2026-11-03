using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Tests.Domain;

/// <summary>Behavioural contract of the membership of a login in a hosting account.</summary>
public sealed class AccountMembershipTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Owning an account yields an owner membership stamped with the given identity and instant.</summary>
    [Fact]
    public void Owning_an_account_yields_an_owner_membership_with_no_grants()
    {
        var id = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        var membership = AccountMembership.Own(id, userId, accountId, Now);

        Assert.Equal(id, membership.Id);
        Assert.Equal(userId, membership.UserId);
        Assert.Equal(accountId, membership.AccountId);
        Assert.Equal(MembershipRole.Owner, membership.Role);
        Assert.Equal(AccountFeature.None, membership.Grants);
        Assert.Equal(Now, membership.CreatedAt);
    }

    /// <summary>An owner is allowed every feature although the grants they carry are empty.</summary>
    [Theory]
    [InlineData(AccountFeature.Mail)]
    [InlineData(AccountFeature.Databases)]
    [InlineData(AccountFeature.Backups)]
    public void An_owner_is_allowed_every_feature_whatever_their_grants_say(AccountFeature feature)
    {
        var owner = AccountMembership.Own(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);

        Assert.True(owner.Allows(feature));
    }

    /// <summary>A new membership is active, owner's and member's alike.</summary>
    [Fact]
    public void A_new_membership_is_active()
    {
        var owner = AccountMembership.Own(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);
        var member = AccountMembership.Admit(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), AccountFeature.Mail, Now);

        Assert.Equal(MembershipState.Active, owner.State);
        Assert.Equal(MembershipState.Active, member.State);
    }

    /// <summary>Suspending a membership suspends it and keeps its role and grants; resuming restores it exactly.</summary>
    [Fact]
    public void Suspending_keeps_the_role_and_grants_and_resuming_restores_the_membership()
    {
        var member = AccountMembership.Admit(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), AccountFeature.Mail, Now);

        member.Suspend();

        Assert.Equal(MembershipState.Suspended, member.State);
        Assert.Equal(MembershipRole.Member, member.Role);
        Assert.Equal(AccountFeature.Mail, member.Grants);

        member.Resume();

        Assert.Equal(MembershipState.Active, member.State);
        Assert.True(member.Allows(AccountFeature.Mail));
    }

    /// <summary>Suspending a suspended membership, or resuming an active one, changes nothing.</summary>
    [Fact]
    public void Suspending_twice_or_resuming_an_active_membership_is_harmless()
    {
        var owner = AccountMembership.Own(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);

        owner.Resume();
        Assert.Equal(MembershipState.Active, owner.State);

        owner.Suspend();
        owner.Suspend();
        Assert.Equal(MembershipState.Suspended, owner.State);
    }

    /// <summary>A suspended membership allows no feature, whatever its role: an owner is not exempt.</summary>
    [Theory]
    [InlineData(AccountFeature.Mail)]
    [InlineData(AccountFeature.Databases)]
    public void A_suspended_membership_allows_no_feature_even_to_an_owner(AccountFeature feature)
    {
        var owner = AccountMembership.Own(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);
        var member = AccountMembership.Admit(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), feature, Now);
        Assert.True(owner.Allows(feature));
        Assert.True(member.Allows(feature));

        owner.Suspend();
        member.Suspend();

        Assert.False(owner.Allows(feature));
        Assert.False(member.Allows(feature));
    }
}
