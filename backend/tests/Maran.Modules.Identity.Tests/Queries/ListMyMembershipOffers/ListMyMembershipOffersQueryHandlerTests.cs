using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Queries.ListMyMembershipOffers;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Tests.Queries.ListMyMembershipOffers;

/// <summary>Behavioural contract of the handler that lists the caller's own open offers.</summary>
public sealed class ListMyMembershipOffersQueryHandlerTests : IDisposable
{
    private readonly MembershipOfferWorld _offers = new();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _offers.Dispose();
    }

    private ListMyMembershipOffersQueryHandler NewHandler(StubCustomerUser caller)
    {
        return new ListMyMembershipOffersQueryHandler(
            _offers.World.Context, IdentityTestContext.DisplayNameResources(), _offers.World.Clock, caller);
    }

    /// <summary>The caller sees their own open offer with the account's name, the features by display name and the expiry.</summary>
    [Fact]
    public async Task The_caller_sees_their_own_open_offer_with_the_features_named()
    {
        var person = await _offers.SeedLoginAsync("bob");
        var offer = await _offers.SeedOfferAsync(person, AccountFeature.Databases | AccountFeature.Cron);

        var result = await NewHandler(MembershipOfferWorld.CallerFor(person)).HandleAsync(
            new ListMyMembershipOffersQuery(), CancellationToken.None);

        var row = Assert.Single(result.Value);
        Assert.Equal(offer.Id, row.Id);
        Assert.Equal("acme", row.AccountName);
        Assert.Equal([AccountFeature.Databases, AccountFeature.Cron], row.Features.Select(feature => { return feature.Feature; }));
        Assert.All(row.Features, feature => { Assert.False(string.IsNullOrWhiteSpace(feature.FeatureDisplayName)); });
        Assert.Equal(offer.ExpiresAt, row.ExpiresAt);
    }

    /// <summary>Another person's offer, an expired one and an answered one are not listed.</summary>
    [Fact]
    public async Task Other_peoples_expired_and_answered_offers_are_not_listed()
    {
        var person = await _offers.SeedLoginAsync("bob");
        var stranger = await _offers.SeedLoginAsync("mallory");
        await _offers.SeedOfferAsync(stranger);
        var declined = await _offers.SeedOfferAsync(person);
        declined.Decline(_offers.World.Clock.UtcNow);
        await _offers.World.Context.SaveChangesAsync();
        var expired = AccountMembershipOffer.Offer(
            Guid.NewGuid(), person.Id, Guid.NewGuid(), "old", "bob", AccountFeature.None, SubUserWorld.Now.AddDays(-30));
        _offers.World.Context.AccountMembershipOffers.Add(expired);
        await _offers.World.Context.SaveChangesAsync();

        var result = await NewHandler(MembershipOfferWorld.CallerFor(person)).HandleAsync(
            new ListMyMembershipOffersQuery(), CancellationToken.None);

        Assert.Empty(result.Value);
    }

    /// <summary>A login with no account chosen is answered normally, with an empty list when it has no offers.</summary>
    [Fact]
    public async Task A_login_with_no_offers_and_no_account_gets_an_empty_list()
    {
        var person = await _offers.SeedLoginAsync("bob");

        var result = await NewHandler(MembershipOfferWorld.CallerFor(person)).HandleAsync(
            new ListMyMembershipOffersQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }
}
