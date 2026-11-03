using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Tests.Domain;

/// <summary>Behavioural contract of <see cref="AccountMembershipOffer"/>.</summary>
public sealed class AccountMembershipOfferTests
{
    /// <summary>The instant offers in these tests are made.</summary>
    private static readonly DateTimeOffset Made = new(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Makes an open offer for the features given.</summary>
    private static AccountMembershipOffer NewOffer(AccountFeature grants = AccountFeature.Databases)
    {
        return AccountMembershipOffer.Offer(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "acme", "deploy", grants, Made);
    }

    /// <summary>A new offer is open and expires fourteen days after it was made.</summary>
    [Fact]
    public void A_new_offer_is_open_and_expires_fourteen_days_later()
    {
        var offer = NewOffer();

        Assert.True(offer.IsOpen(Made));
        Assert.Equal(Made.AddDays(14), offer.ExpiresAt);
        Assert.Null(offer.ResolvedAt);
        Assert.Null(offer.Outcome);
    }

    /// <summary>An offer is no longer open from the instant it expires.</summary>
    [Fact]
    public void An_offer_is_not_open_from_the_instant_it_expires()
    {
        var offer = NewOffer();

        Assert.True(offer.IsOpen(offer.ExpiresAt.AddTicks(-1)));
        Assert.False(offer.IsOpen(offer.ExpiresAt));
    }

    /// <summary>Accepting builds a member membership from the offer's own grants and resolves the offer.</summary>
    [Fact]
    public void Accepting_builds_a_member_membership_from_the_offers_own_grants()
    {
        var offer = NewOffer(AccountFeature.Databases | AccountFeature.Cron);
        var membershipId = Guid.NewGuid();

        var membership = offer.Accept(membershipId, Made.AddHours(1));

        Assert.Equal(membershipId, membership.Id);
        Assert.Equal(offer.UserId, membership.UserId);
        Assert.Equal(offer.AccountId, membership.AccountId);
        Assert.Equal(MembershipRole.Member, membership.Role);
        Assert.Equal(MembershipState.Active, membership.State);
        Assert.Equal(AccountFeature.Databases | AccountFeature.Cron, membership.Grants);
        Assert.Equal(MembershipOfferOutcome.Accepted, offer.Outcome);
        Assert.Equal(Made.AddHours(1), offer.ResolvedAt);
    }

    /// <summary>An offer that was accepted cannot be accepted or declined again.</summary>
    [Fact]
    public void An_accepted_offer_cannot_be_answered_again()
    {
        var offer = NewOffer();
        offer.Accept(Guid.NewGuid(), Made.AddHours(1));

        Assert.Throws<InvalidOperationException>(() => { offer.Accept(Guid.NewGuid(), Made.AddHours(2)); });
        Assert.Throws<InvalidOperationException>(() => { offer.Decline(Made.AddHours(2)); });
    }

    /// <summary>An expired offer cannot be accepted or declined.</summary>
    [Fact]
    public void An_expired_offer_cannot_be_answered()
    {
        var offer = NewOffer();
        var late = offer.ExpiresAt;

        Assert.Throws<InvalidOperationException>(() => { offer.Accept(Guid.NewGuid(), late); });
        Assert.Throws<InvalidOperationException>(() => { offer.Decline(late); });
        Assert.Null(offer.ResolvedAt);
    }

    /// <summary>Declining resolves the offer as declined and builds nothing.</summary>
    [Fact]
    public void Declining_resolves_the_offer_as_declined()
    {
        var offer = NewOffer();

        offer.Decline(Made.AddHours(1));

        Assert.Equal(MembershipOfferOutcome.Declined, offer.Outcome);
        Assert.False(offer.IsOpen(Made.AddHours(2)));
    }

    /// <summary>An owner can withdraw an offer, even one past its expiry, and an answered one stays as it was.</summary>
    [Fact]
    public void Cancelling_withdraws_an_offer_and_leaves_an_answered_one_alone()
    {
        var withdrawn = NewOffer();
        withdrawn.Cancel(withdrawn.ExpiresAt.AddDays(1));
        var declined = NewOffer();
        declined.Decline(Made.AddHours(1));
        declined.Cancel(Made.AddHours(2));

        Assert.Equal(MembershipOfferOutcome.Cancelled, withdrawn.Outcome);
        Assert.Equal(MembershipOfferOutcome.Declined, declined.Outcome);
    }

    /// <summary>Expiring closes an offer only once it has reached its expiry.</summary>
    [Fact]
    public void Expiring_closes_an_offer_only_once_it_has_expired()
    {
        var offer = NewOffer();

        offer.Expire(offer.ExpiresAt.AddTicks(-1));
        Assert.Null(offer.ResolvedAt);

        offer.Expire(offer.ExpiresAt);
        Assert.Equal(MembershipOfferOutcome.Expired, offer.Outcome);
    }

    /// <summary>The stored account name and identifier are shortened to their columns.</summary>
    [Fact]
    public void The_account_name_and_identifier_are_shortened_to_their_columns()
    {
        var offer = AccountMembershipOffer.Offer(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new string('a', 400), new string('b', 400), AccountFeature.None, Made);

        Assert.Equal(AccountMembershipOffer.AccountNameLength, offer.AccountName.Length);
        Assert.Equal(AccountMembershipOffer.IdentifierLength, offer.Identifier.Length);
    }
}
