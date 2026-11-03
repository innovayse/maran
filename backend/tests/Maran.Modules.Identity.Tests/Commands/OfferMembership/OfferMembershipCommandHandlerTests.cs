using Maran.Modules.Identity.Commands.OfferMembership;
using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Identity.Tests.Commands.OfferMembership;

/// <summary>Behavioural contract of the handler that offers an existing login a place on the team.</summary>
/// <remarks>
/// UNOBSERVED HERE: that a real rollback undoes the decoy, that the one-open-offer index refuses a racing
/// second offer and that the team lock serialises two requests. All three are PostgreSQL's; the
/// emulated lock only shows what the handler asks of it.
/// </remarks>
public sealed class OfferMembershipCommandHandlerTests : IDisposable
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

    private static OfferMembershipCommand Command(string identifier, params AccountFeature[] features)
    {
        return new OfferMembershipCommand(identifier, features, "203.0.113.7", "agent");
    }

    /// <summary>Offering a login that qualifies stores one open offer with the chosen features and creates no membership.</summary>
    [Fact]
    public async Task Offering_a_qualifying_login_stores_an_open_offer_and_grants_nothing()
    {
        var person = await _offers.SeedLoginAsync("bob");

        var result = await _offers.NewOfferHandler().HandleAsync(
            Command("bob", AccountFeature.Databases, AccountFeature.Cron), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var offer = await World.Context.AccountMembershipOffers.SingleAsync();
        Assert.Equal(person.Id, offer.UserId);
        Assert.Equal(SubUserWorld.AccountId, offer.AccountId);
        Assert.Equal(AccountFeature.Databases | AccountFeature.Cron, offer.Grants);
        Assert.True(offer.IsOpen(World.Clock.UtcNow));
        Assert.False(await World.Context.AccountMemberships.AnyAsync(candidate => candidate.UserId == person.Id));
    }

    /// <summary>The login is found by its address as well as by its name.</summary>
    [Fact]
    public async Task A_login_is_found_by_its_address_as_well_as_its_name()
    {
        var person = await _offers.SeedLoginAsync("bob");

        await _offers.NewOfferHandler().HandleAsync(Command("bob@example.com"), CancellationToken.None);

        Assert.Equal(person.Id, (await World.Context.AccountMembershipOffers.SingleAsync()).UserId);
    }

    /// <summary>The mail goes to the stored address of the login, names no token, and is published once.</summary>
    [Fact]
    public async Task The_mail_goes_to_the_stored_address_of_the_login_and_carries_no_token()
    {
        await _offers.SeedLoginAsync("bob");

        await _offers.NewOfferHandler().HandleAsync(Command("bob"), CancellationToken.None);

        var mail = Assert.IsType<SendMailRequested>(Assert.Single(World.Bus.Published));
        Assert.Equal("bob@example.com", mail.Recipient);
        Assert.Equal("MembershipOfferSubject:{0}", mail.Subject);
        Assert.DoesNotContain("Invitation", mail.Body, StringComparison.Ordinal);
        Assert.False(await World.Context.InvitationTokens.AnyAsync());
    }

    /// <summary>The answer is an echo of what the owner typed.</summary>
    [Fact]
    public async Task The_answer_echoes_what_the_owner_typed()
    {
        await _offers.SeedLoginAsync("bob");

        var result = await _offers.NewOfferHandler().HandleAsync(Command("bob"), CancellationToken.None);

        Assert.Equal(new MembershipOfferRequestedDto("bob"), result.Value);
    }

    /// <summary>
    /// Every request that cannot be honoured answers exactly as an honoured one does, stores nothing
    /// and mails nobody, so the form cannot be used to learn which logins exist.
    /// </summary>
    [Theory]
    [InlineData("nobody")]
    [InlineData("nobody@example.org")]
    [InlineData("an-admin")]
    [InlineData("invited")]
    [InlineData("member")]
    [InlineData("acme")]
    public async Task A_request_that_cannot_be_honoured_answers_like_one_that_was_and_does_nothing(string identifier)
    {
        await _offers.SeedLoginAsync("an-admin", UserRole.Admin);
        await _offers.SeedInvitedLoginAsync("invited");
        await World.SeedMemberAsync("member", AccountFeature.All);
        await _offers.SeedLoginAsync("bob");
        var honoured = await _offers.NewOfferHandler().HandleAsync(Command("bob"), CancellationToken.None);
        var before = await World.Context.AccountMembershipOffers.CountAsync();

        var refused = await _offers.NewOfferHandler().HandleAsync(Command(identifier), CancellationToken.None);

        Assert.True(honoured.IsSuccess);
        Assert.True(refused.IsSuccess);
        Assert.Equal(honoured.Value.GetType(), refused.Value.GetType());
        Assert.Equal(identifier, refused.Value.Identifier);
        Assert.Equal(before, await World.Context.AccountMembershipOffers.CountAsync());
        Assert.Single(World.Bus.Published);
    }

    /// <summary>An account that is suspended is offered no new members; the request still answers as a success.</summary>
    [Fact]
    public async Task A_suspended_account_stores_no_offer_and_still_answers_success()
    {
        World.Accounts.Add(SubUserWorld.AccountId, "acme", maxFtpUsers: 1, AccountState.Suspended);
        await _offers.SeedLoginAsync("bob");

        var result = await _offers.NewOfferHandler().HandleAsync(Command("bob"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(World.Context.AccountMembershipOffers);
        Assert.Empty(World.Bus.Published);
    }

    /// <summary>A second offer to the same login while one is open stores nothing new.</summary>
    [Fact]
    public async Task A_second_offer_while_one_is_open_stores_nothing_new()
    {
        await _offers.SeedLoginAsync("bob");
        await _offers.NewOfferHandler().HandleAsync(Command("bob"), CancellationToken.None);

        var again = await _offers.NewOfferHandler().HandleAsync(Command("bob", AccountFeature.Cron), CancellationToken.None);

        Assert.True(again.IsSuccess);
        Assert.Single(World.Context.AccountMembershipOffers);
        Assert.Single(World.Bus.Published);
    }

    /// <summary>
    /// An offer that expired unanswered is closed and a fresh one is stored in its place, and the
    /// answer is the same as for any other request.
    /// </summary>
    [Fact]
    public async Task An_expired_unanswered_offer_is_closed_and_a_fresh_one_is_stored()
    {
        await _offers.SeedLoginAsync("bob");
        var first = await _offers.NewOfferHandler().HandleAsync(Command("bob"), CancellationToken.None);
        World.Clock.Advance(AccountMembershipOffer.Lifetime + TimeSpan.FromMinutes(1));

        var second = await _offers.NewOfferHandler().HandleAsync(Command("bob", AccountFeature.Cron), CancellationToken.None);

        Assert.Equal(first.Value, second.Value);
        var stored = await World.Context.AccountMembershipOffers.OrderBy(candidate => candidate.CreatedAt).ToListAsync();
        Assert.Equal(2, stored.Count);
        Assert.Equal(MembershipOfferOutcome.Expired, stored[0].Outcome);
        Assert.True(stored[1].IsOpen(World.Clock.UtcNow));
        Assert.Equal(AccountFeature.Cron, stored[1].Grants);
    }

    /// <summary>A login that declined can be offered again.</summary>
    [Fact]
    public async Task A_login_that_declined_can_be_offered_again()
    {
        var person = await _offers.SeedLoginAsync("bob");
        var declined = await _offers.SeedOfferAsync(person);
        declined.Decline(World.Clock.UtcNow);
        await World.Context.SaveChangesAsync();

        await _offers.NewOfferHandler().HandleAsync(Command("bob"), CancellationToken.None);

        Assert.Equal(2, await World.Context.AccountMembershipOffers.CountAsync());
        Assert.Single(World.Bus.Published);
    }

    /// <summary>Open offers count toward the cap, and a full team is told so whatever was asked for.</summary>
    [Fact]
    public async Task Open_offers_count_toward_the_cap_and_the_refusal_names_only_the_owners_own_team()
    {
        await _offers.SeedLoginAsync("bob");
        await _offers.SeedLoginAsync("carol");
        await _offers.NewOfferHandler(maxPerAccount: 1).HandleAsync(Command("bob"), CancellationToken.None);

        var toExisting = await _offers.NewOfferHandler(maxPerAccount: 1).HandleAsync(Command("carol"), CancellationToken.None);
        var toNobody = await _offers.NewOfferHandler(maxPerAccount: 1).HandleAsync(Command("nobody"), CancellationToken.None);

        Assert.Equal("SubUserLimitReached", toExisting.Error!.Code);
        Assert.Equal("SubUserLimitReached", toNobody.Error!.Code);
        Assert.Single(World.Context.AccountMembershipOffers);
    }

    /// <summary>An expired offer does not count toward the cap.</summary>
    [Fact]
    public async Task An_expired_offer_does_not_count_toward_the_cap()
    {
        var person = await _offers.SeedLoginAsync("bob");
        await _offers.SeedLoginAsync("carol");
        await _offers.SeedOfferAsync(person);
        World.Clock.Advance(AccountMembershipOffer.Lifetime + TimeSpan.FromMinutes(1));

        var result = await _offers.NewOfferHandler(maxPerAccount: 1).HandleAsync(Command("carol"), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>A team member cannot offer anybody a place, and nothing is stored or mailed.</summary>
    [Fact]
    public async Task A_member_cannot_offer()
    {
        var member = await World.SeedMemberAsync("helper", AccountFeature.All);
        await _offers.SeedLoginAsync("bob");
        var caller = new StubCustomerUser(member.Id, member.Username, SubUserWorld.AccountId);

        var result = await _offers.NewOfferHandler(caller).HandleAsync(Command("bob"), CancellationToken.None);

        Assert.Equal("SubUsersOwnerOnly", result.Error!.Code);
        Assert.Empty(World.Context.AccountMembershipOffers);
        Assert.Empty(World.Bus.Published);
    }

    /// <summary>An administrator, who owns no account, cannot offer anybody a place.</summary>
    [Fact]
    public async Task A_login_acting_for_no_account_cannot_offer()
    {
        var accountless = new StubCustomerUser(Guid.NewGuid(), "nobody-here", accountId: null);

        var result = await _offers.NewOfferHandler(accountless).HandleAsync(Command("bob"), CancellationToken.None);

        Assert.Equal("SubUsersOwnerOnly", result.Error!.Code);
    }

    /// <summary>The real outcome is journalled as unsuccessful for a no-op and successful for an offer, with what was typed.</summary>
    [Fact]
    public async Task The_real_outcome_is_journalled_with_what_the_owner_typed()
    {
        await _offers.SeedLoginAsync("bob");

        await _offers.NewOfferHandler().HandleAsync(Command("bob"), CancellationToken.None);
        await _offers.NewOfferHandler().HandleAsync(Command("nobody@example.org"), CancellationToken.None);

        Assert.Collection(
            World.Audit.Written,
            honoured =>
            {
                Assert.Equal(IdentityAuditActions.SubUserMembershipOffered, honoured.Action);
                Assert.Equal("bob", honoured.Subject);
                Assert.Equal(World.Owner.Id, honoured.ActorUserId);
                Assert.True(honoured.Succeeded);
            },
            noop =>
            {
                Assert.Equal(IdentityAuditActions.SubUserMembershipOffered, noop.Action);
                Assert.Equal("nobody@example.org", noop.Subject);
                Assert.False(noop.Succeeded);
            });
    }

    /// <summary>
    /// A request that does not qualify does the work of one that does in the same shape: it stages an
    /// offer and renders the mail, then undoes it, while an honoured one commits.
    /// </summary>
    [Fact]
    public async Task A_request_that_does_not_qualify_stages_an_offer_and_rolls_it_back()
    {
        await _offers.SeedLoginAsync("bob");
        using var honouredWorld = new MembershipOfferWorld();
        await honouredWorld.SeedLoginAsync("bob");
        await honouredWorld.NewOfferHandler().HandleAsync(Command("bob"), CancellationToken.None);

        await _offers.NewOfferHandler().HandleAsync(Command("nobody"), CancellationToken.None);

        Assert.Equal((1, 0), (honouredWorld.World.TeamLock.Commits, honouredWorld.World.TeamLock.Rollbacks));
        Assert.Equal((0, 1), (World.TeamLock.Commits, World.TeamLock.Rollbacks));
        Assert.Equal(1, World.TeamLock.RowsRolledBack);
        Assert.Empty(World.Context.AccountMembershipOffers);
    }
}
