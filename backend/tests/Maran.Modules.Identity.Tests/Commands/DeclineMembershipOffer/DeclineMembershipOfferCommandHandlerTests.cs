using Maran.Modules.Identity.Commands.DeclineMembershipOffer;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Identity.Tests.Commands.DeclineMembershipOffer;

/// <summary>Behavioural contract of the handler that turns an offer down.</summary>
public sealed class DeclineMembershipOfferCommandHandlerTests : IDisposable
{
    private readonly MembershipOfferWorld _offers = new();

    private SubUserWorld World
    {
        get
        {
            return _offers.World;
        }
    }

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _offers.Dispose();
    }

    private static DeclineMembershipOfferCommand Command(Guid offerId)
    {
        return new DeclineMembershipOfferCommand(offerId, "203.0.113.9", "agent");
    }

    /// <summary>Declining closes the offer, creates no membership and is journalled.</summary>
    [Fact]
    public async Task Declining_closes_the_offer_and_creates_no_membership()
    {
        var person = await _offers.SeedLoginAsync("bob");
        var offer = await _offers.SeedOfferAsync(person);

        var result = await _offers.NewDeclineHandler(MembershipOfferWorld.CallerFor(person)).HandleAsync(
            Command(offer.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(MembershipOfferOutcome.Declined, (await World.Context.AccountMembershipOffers.SingleAsync()).Outcome);
        Assert.False(await World.Context.AccountMemberships.AnyAsync(candidate => candidate.UserId == person.Id));
        var entry = Assert.Single(World.Audit.Written);
        Assert.Equal(IdentityAuditActions.SubUserMembershipOfferDeclined, entry.Action);
        Assert.True(entry.Succeeded);
    }

    /// <summary>Somebody else's offer is not found and stays open.</summary>
    [Fact]
    public async Task Another_persons_offer_is_not_found_and_stays_open()
    {
        var person = await _offers.SeedLoginAsync("bob");
        var stranger = await _offers.SeedLoginAsync("mallory");
        var offer = await _offers.SeedOfferAsync(person);

        var result = await _offers.NewDeclineHandler(MembershipOfferWorld.CallerFor(stranger)).HandleAsync(
            Command(offer.Id), CancellationToken.None);

        Assert.Equal("MembershipOfferNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.True((await World.Context.AccountMembershipOffers.SingleAsync()).IsOpen(World.Clock.UtcNow));
        Assert.False(Assert.Single(World.Audit.Written).Succeeded);
    }

    /// <summary>An offer past its expiry is not found.</summary>
    [Fact]
    public async Task An_expired_offer_is_not_found()
    {
        var person = await _offers.SeedLoginAsync("bob");
        var expired = await _offers.SeedOfferAsync(person);
        World.Clock.Advance(AccountMembershipOffer.Lifetime);
        var caller = MembershipOfferWorld.CallerFor(person);

        var late = await _offers.NewDeclineHandler(caller).HandleAsync(Command(expired.Id), CancellationToken.None);

        Assert.Equal("MembershipOfferNotFound", late.Error!.Code);
    }
}
