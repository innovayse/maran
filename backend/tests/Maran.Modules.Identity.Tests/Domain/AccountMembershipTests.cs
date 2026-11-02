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
}
