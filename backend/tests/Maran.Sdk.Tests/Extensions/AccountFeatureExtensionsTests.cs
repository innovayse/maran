using Maran.Sdk.Contracts;
using Maran.Sdk.Extensions;

namespace Maran.Sdk.Tests.Extensions;

/// <summary>Behavioral contract of <see cref="AccountFeatureExtensions"/>, the one place a feature set is decoded.</summary>
public sealed class AccountFeatureExtensionsTests
{
    /// <summary>The full set lists every feature in declaration order and neither None nor All.</summary>
    [Fact]
    public void The_full_set_lists_every_feature_in_declaration_order()
    {
        Assert.Equal(
            [
                AccountFeature.Mail,
                AccountFeature.Dns,
                AccountFeature.Databases,
                AccountFeature.Cron,
                AccountFeature.FileTransfer,
                AccountFeature.Backups,
            ],
            AccountFeature.All.Members());
    }

    /// <summary>A subset lists only its own members, and the empty set lists none.</summary>
    [Fact]
    public void A_subset_lists_only_its_own_members_and_the_empty_set_none()
    {
        Assert.Equal(
            [AccountFeature.Dns, AccountFeature.Backups],
            (AccountFeature.Dns | AccountFeature.Backups).Members());
        Assert.Empty(AccountFeature.None.Members());
    }

    /// <summary>Only a single defined member is a feature.</summary>
    [Theory]
    [InlineData(1L, true)]
    [InlineData(32L, true)]
    [InlineData(0L, false)]
    [InlineData(3L, false)]
    [InlineData(63L, false)]
    [InlineData(64L, false)]
    public void Only_a_single_defined_member_is_a_feature(long raw, bool expected)
    {
        Assert.Equal(expected, ((AccountFeature)raw).IsFeature());
    }

    /// <summary>A set is known when it holds no bit outside the defined features.</summary>
    [Fact]
    public void A_set_is_known_when_it_holds_no_bit_outside_the_defined_features()
    {
        Assert.True(AccountFeature.None.IsKnownSet());
        Assert.True(AccountFeature.All.IsKnownSet());
        Assert.False(((AccountFeature)64).IsKnownSet());
    }

    /// <summary>A list of features becomes their union, and a round trip through Members is lossless.</summary>
    [Fact]
    public void A_list_of_features_becomes_their_union_and_the_round_trip_is_lossless()
    {
        var set = AccountFeature.Mail | AccountFeature.Cron | AccountFeature.Backups;

        Assert.Equal(set, set.Members().ToSet());
        Assert.Equal(AccountFeature.Mail, new[] { AccountFeature.Mail, AccountFeature.Mail }.ToSet());
        Assert.Equal(AccountFeature.None, Array.Empty<AccountFeature>().ToSet());
    }
}
