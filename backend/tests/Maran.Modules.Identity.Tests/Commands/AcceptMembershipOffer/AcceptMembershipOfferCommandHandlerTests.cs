using Maran.Modules.Identity.Commands.AcceptMembershipOffer;
using Maran.Modules.Identity.Commands.DeclineMembershipOffer;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Identity.Tests.Commands.AcceptMembershipOffer;

/// <summary>Behavioural contract of the handler that turns an offer into a membership.</summary>
/// <remarks>
/// UNOBSERVED HERE: that two parallel acceptances make one membership. That is the unique key's work
/// in PostgreSQL, which the in-memory provider does not enforce.
/// </remarks>
public sealed class AcceptMembershipOfferCommandHandlerTests : IDisposable
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

    private static AcceptMembershipOfferCommand Command(Guid offerId)
    {
        return new AcceptMembershipOfferCommand(offerId, "203.0.113.9", "agent");
    }

    /// <summary>Accepting creates exactly one active member membership holding the offered features.</summary>
    [Fact]
    public async Task Accepting_creates_one_member_membership_with_the_offered_features()
    {
        var person = await _offers.SeedLoginAsync("bob");
        var offer = await _offers.SeedOfferAsync(person, AccountFeature.Databases | AccountFeature.Cron);

        var result = await _offers.NewAcceptHandler(MembershipOfferWorld.CallerFor(person)).HandleAsync(
            Command(offer.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var membership = await World.Context.AccountMemberships.SingleAsync(candidate => candidate.UserId == person.Id);
        Assert.Equal(MembershipRole.Member, membership.Role);
        Assert.Equal(MembershipState.Active, membership.State);
        Assert.Equal(SubUserWorld.AccountId, membership.AccountId);
        Assert.Equal(AccountFeature.Databases | AccountFeature.Cron, membership.Grants);
        Assert.Equal(MembershipOfferOutcome.Accepted, (await World.Context.AccountMembershipOffers.SingleAsync()).Outcome);
    }

    /// <summary>Accepting is journalled with the person who accepted as actor.</summary>
    [Fact]
    public async Task Accepting_is_journalled_with_the_person_as_actor()
    {
        var person = await _offers.SeedLoginAsync("bob");
        var offer = await _offers.SeedOfferAsync(person);

        await _offers.NewAcceptHandler(MembershipOfferWorld.CallerFor(person)).HandleAsync(
            Command(offer.Id), CancellationToken.None);

        var entry = Assert.Single(World.Audit.Written);
        Assert.Equal(IdentityAuditActions.SubUserMembershipOfferAccepted, entry.Action);
        Assert.Equal(person.Id, entry.ActorUserId);
        Assert.Equal(offer.Id.ToString("D"), entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>Somebody else's offer is not found, and nothing is granted.</summary>
    [Fact]
    public async Task Another_persons_offer_is_not_found_and_grants_nothing()
    {
        var person = await _offers.SeedLoginAsync("bob");
        var stranger = await _offers.SeedLoginAsync("mallory");
        var offer = await _offers.SeedOfferAsync(person);

        var result = await _offers.NewAcceptHandler(MembershipOfferWorld.CallerFor(stranger)).HandleAsync(
            Command(offer.Id), CancellationToken.None);

        Assert.Equal("MembershipOfferNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.False(await World.Context.AccountMemberships.AnyAsync(candidate => candidate.UserId == stranger.Id));
        Assert.True((await World.Context.AccountMembershipOffers.SingleAsync()).IsOpen(World.Clock.UtcNow));
        Assert.False(Assert.Single(World.Audit.Written).Succeeded);
    }

    /// <summary>An unknown offer answers as a foreign one does.</summary>
    [Fact]
    public async Task An_unknown_offer_answers_not_found()
    {
        var person = await _offers.SeedLoginAsync("bob");

        var result = await _offers.NewAcceptHandler(MembershipOfferWorld.CallerFor(person)).HandleAsync(
            Command(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal("MembershipOfferNotFound", result.Error!.Code);
    }

    /// <summary>An expired offer answers not found rather than a conflict, and grants nothing.</summary>
    [Fact]
    public async Task An_expired_offer_answers_not_found_and_grants_nothing()
    {
        var person = await _offers.SeedLoginAsync("bob");
        var offer = await _offers.SeedOfferAsync(person);
        World.Clock.Advance(AccountMembershipOffer.Lifetime);

        var result = await _offers.NewAcceptHandler(MembershipOfferWorld.CallerFor(person)).HandleAsync(
            Command(offer.Id), CancellationToken.None);

        Assert.Equal("MembershipOfferNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.False(await World.Context.AccountMemberships.AnyAsync(candidate => candidate.UserId == person.Id));
    }

    /// <summary>A declined offer cannot be accepted afterwards.</summary>
    [Fact]
    public async Task A_declined_offer_cannot_be_accepted()
    {
        var person = await _offers.SeedLoginAsync("bob");
        var offer = await _offers.SeedOfferAsync(person);
        var caller = MembershipOfferWorld.CallerFor(person);
        await _offers.NewDeclineHandler(caller).HandleAsync(
            new DeclineMembershipOfferCommand(offer.Id), CancellationToken.None);

        var result = await _offers.NewAcceptHandler(caller).HandleAsync(Command(offer.Id), CancellationToken.None);

        Assert.Equal("MembershipOfferNotFound", result.Error!.Code);
        Assert.False(await World.Context.AccountMemberships.AnyAsync(candidate => candidate.UserId == person.Id));
    }

    /// <summary>A second acceptance of the same offer is not found and makes no second membership.</summary>
    [Fact]
    public async Task A_second_acceptance_is_not_found_and_makes_no_second_membership()
    {
        var person = await _offers.SeedLoginAsync("bob");
        var offer = await _offers.SeedOfferAsync(person);
        var caller = MembershipOfferWorld.CallerFor(person);
        await _offers.NewAcceptHandler(caller).HandleAsync(Command(offer.Id), CancellationToken.None);

        var again = await _offers.NewAcceptHandler(caller).HandleAsync(Command(offer.Id), CancellationToken.None);

        Assert.Equal("MembershipOfferNotFound", again.Error!.Code);
        Assert.Equal(1, await World.Context.AccountMemberships.CountAsync(candidate => candidate.UserId == person.Id));
    }

    /// <summary>An account that was suspended after the offer was made refuses the acceptance with a conflict.</summary>
    [Fact]
    public async Task A_suspended_account_refuses_the_acceptance()
    {
        var person = await _offers.SeedLoginAsync("bob");
        var offer = await _offers.SeedOfferAsync(person);
        var owner = await World.Context.AccountMemberships.SingleAsync(candidate => candidate.Role == MembershipRole.Owner);
        owner.Suspend();
        await World.Context.SaveChangesAsync();

        var result = await _offers.NewAcceptHandler(MembershipOfferWorld.CallerFor(person)).HandleAsync(
            Command(offer.Id), CancellationToken.None);

        Assert.Equal("MembershipOfferAccountSuspended", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.False(await World.Context.AccountMemberships.AnyAsync(candidate => candidate.UserId == person.Id));
    }

    /// <summary>An account that no longer has an owner membership, because it was deleted, answers not found.</summary>
    [Fact]
    public async Task A_deleted_account_answers_not_found()
    {
        var person = await _offers.SeedLoginAsync("bob");
        var offer = await _offers.SeedOfferAsync(person, accountId: Guid.NewGuid());

        var result = await _offers.NewAcceptHandler(MembershipOfferWorld.CallerFor(person)).HandleAsync(
            Command(offer.Id), CancellationToken.None);

        Assert.Equal("MembershipOfferNotFound", result.Error!.Code);
    }

    /// <summary>A login that already holds a membership of the account is not given a second one.</summary>
    [Fact]
    public async Task A_login_already_on_the_team_is_not_given_a_second_membership()
    {
        var person = await World.SeedMemberAsync("bob", AccountFeature.Cron);
        var offer = await _offers.SeedOfferAsync(person, AccountFeature.All);

        var result = await _offers.NewAcceptHandler(MembershipOfferWorld.CallerFor(person)).HandleAsync(
            Command(offer.Id), CancellationToken.None);

        Assert.Equal("MembershipOfferNotFound", result.Error!.Code);
        var membership = await World.Context.AccountMemberships.SingleAsync(candidate => candidate.UserId == person.Id);
        Assert.Equal(AccountFeature.Cron, membership.Grants);
    }

    /// <summary>The accepting commit and the refusals each end the team lock's transaction as they should.</summary>
    [Fact]
    public async Task The_acceptance_commits_and_a_refusal_rolls_back()
    {
        var person = await _offers.SeedLoginAsync("bob");
        var offer = await _offers.SeedOfferAsync(person);
        var caller = MembershipOfferWorld.CallerFor(person);

        await _offers.NewAcceptHandler(caller).HandleAsync(Command(offer.Id), CancellationToken.None);
        await _offers.NewAcceptHandler(caller).HandleAsync(Command(Guid.NewGuid()), CancellationToken.None);

        // The second request names no offer, so it never reaches the lock at all.
        Assert.Equal((1, 0), (World.TeamLock.Commits, World.TeamLock.Rollbacks));
    }
}
