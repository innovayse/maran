using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Queries.ListSubUsers;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Pagination;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Identity.Tests.Queries.ListSubUsers;

/// <summary>Behavioural contract of the sub-users list handler.</summary>
/// <remarks>
/// UNOBSERVED HERE: the search. It is an <c>ILIKE</c>, which only PostgreSQL translates, so the
/// InMemory provider cannot run it; <c>SubUsersEndpointTests</c> covers it against a real server.
/// </remarks>
public sealed class ListSubUsersQueryHandlerTests : IDisposable
{
    private readonly SubUserWorld _world = new();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _world.Dispose();
    }

    private ListSubUsersQueryHandler NewHandler(StubCustomerUser? caller = null)
    {
        return new ListSubUsersQueryHandler(_world.Context, _world.Guard(caller), _world.Clock);
    }

    /// <summary>The owner sees their own members with the features they hold, and not themselves.</summary>
    [Fact]
    public async Task The_owner_sees_their_members_and_not_themselves()
    {
        await _world.SeedMemberAsync("deploy", AccountFeature.Databases | AccountFeature.Backups);

        var result = await NewHandler().HandleAsync(new ListSubUsersQuery(), CancellationToken.None);

        var row = Assert.Single(result.Value.Items);
        Assert.Equal("deploy", row.Username);
        Assert.Equal(UserLifecycle.Active, row.State);
        Assert.False(row.IsOffer);
        Assert.Equal([AccountFeature.Databases, AccountFeature.Backups], row.Features);
        Assert.Equal(1, result.Value.Total);
    }

    /// <summary>Another account's members are not in the answer or in its total.</summary>
    [Fact]
    public async Task Another_accounts_members_are_not_listed()
    {
        await _world.SeedMemberAsync("mine", AccountFeature.Databases);
        await _world.SeedMemberAsync("theirs", AccountFeature.Databases, accountId: Guid.NewGuid());

        var result = await NewHandler().HandleAsync(new ListSubUsersQuery(), CancellationToken.None);

        Assert.Equal(["mine"], result.Value.Items.Select(row => { return row.Username; }));
        Assert.Equal(1, result.Value.Total);
    }

    /// <summary>The page is cut by the requested size and the total counts every row.</summary>
    [Fact]
    public async Task The_page_is_cut_and_the_total_counts_every_row()
    {
        await _world.SeedMemberAsync("a", AccountFeature.None);
        await _world.SeedMemberAsync("b", AccountFeature.None);
        await _world.SeedMemberAsync("c", AccountFeature.None);

        var result = await NewHandler().HandleAsync(new ListSubUsersQuery(PageSize: 10), CancellationToken.None);

        Assert.Equal(3, result.Value.Total);
        Assert.Equal(3, result.Value.Items.Count);
    }

    /// <summary>A team member may not list the team.</summary>
    [Fact]
    public async Task A_member_is_refused()
    {
        var member = await _world.SeedMemberAsync("deploy", AccountFeature.All);
        var caller = new StubCustomerUser(member.Id, member.Username, SubUserWorld.AccountId);

        var result = await NewHandler(caller).HandleAsync(new ListSubUsersQuery(), CancellationToken.None);

        Assert.Equal("SubUsersOwnerOnly", result.Error!.Code);
    }

    /// <summary>The sort whitelist accepts its own names and refuses an unknown one.</summary>
    [Fact]
    public void The_validator_accepts_a_known_sort_and_refuses_an_unknown_one()
    {
        var validator = new ListSubUsersQueryValidator();

        Assert.True(validator.Validate(new ListSubUsersQuery(Sort: SortRequest.Parse("-username"))).IsValid);
        Assert.False(validator.Validate(new ListSubUsersQuery(Sort: SortRequest.Parse("password"))).IsValid);
    }

    /// <summary>An open offer is listed as an invited row carrying only what the owner typed.</summary>
    [Fact]
    public async Task An_open_offer_is_listed_as_an_invited_row_carrying_only_what_the_owner_typed()
    {
        var person = await SeedOfferedLoginAsync("bob", "bob@example.com");

        var result = await NewHandler().HandleAsync(new ListSubUsersQuery(), CancellationToken.None);

        var row = Assert.Single(result.Value.Items);
        Assert.Equal(UserLifecycle.Invited, row.State);
        Assert.Equal("bob@example.com", row.Username);
        Assert.Equal("bob@example.com", row.Email);
        Assert.NotEqual(person.Id, row.Id);
        Assert.True(row.IsOffer);
        Assert.Null(row.LastLoginAt);
        Assert.Equal([AccountFeature.Databases], row.Features);
    }

    /// <summary>A mixed list marks each offer as an offer and each member as a member.</summary>
    [Fact]
    public async Task A_mixed_list_marks_offers_and_members_correctly()
    {
        await _world.SeedMemberAsync("member", AccountFeature.None);
        var person = await SeedOfferedLoginAsync("offered", "offered@example.com");

        var result = await NewHandler().HandleAsync(new ListSubUsersQuery(), CancellationToken.None);

        Assert.Equal(2, result.Value.Items.Count);
        var member = Assert.Single(result.Value.Items, row => { return !row.IsOffer; });
        var offer = Assert.Single(result.Value.Items, row => { return row.IsOffer; });
        Assert.Equal("member", member.Username);
        Assert.Equal("offered@example.com", offer.Username);
        Assert.NotEqual(person.Id, offer.Id);
    }

    /// <summary>Members and offers are paged and counted together, across the page boundary.</summary>
    [Fact]
    public async Task Members_and_offers_are_paged_and_counted_together()
    {
        await _world.SeedMemberAsync("a", AccountFeature.None);
        await _world.SeedMemberAsync("b", AccountFeature.None);
        await SeedOfferedLoginAsync("c", "c");
        await SeedOfferedLoginAsync("d", "d");
        await SeedOfferedLoginAsync("e", "e");

        var first = await NewHandler().HandleAsync(new ListSubUsersQuery(Page: 1, PageSize: 3), CancellationToken.None);
        var second = await NewHandler().HandleAsync(new ListSubUsersQuery(Page: 2, PageSize: 3), CancellationToken.None);

        Assert.Equal(5, first.Value.Total);
        Assert.Equal(3, first.Value.Items.Count);
        Assert.Equal(2, second.Value.Items.Count);
        Assert.Equal(
            5,
            first.Value.Items.Concat(second.Value.Items).Select(row => { return row.Id; }).Distinct().Count());
    }

    /// <summary>An expired offer, an answered one and another account's offer are not listed.</summary>
    [Fact]
    public async Task Expired_answered_and_foreign_offers_are_not_listed()
    {
        await SeedOfferedLoginAsync("old", "old");
        _world.Clock.Advance(AccountMembershipOffer.Lifetime + TimeSpan.FromMinutes(1));
        var answered = await SeedOfferedLoginAsync("done", "done");
        var offer = await _world.Context.AccountMembershipOffers.SingleAsync(candidate => candidate.UserId == answered.Id);
        offer.Decline(_world.Clock.UtcNow);
        var foreign = await _world.SeedMemberAsync("stranger", AccountFeature.None, accountId: Guid.NewGuid());
        _world.Context.AccountMembershipOffers.Add(AccountMembershipOffer.Offer(
            Guid.NewGuid(), foreign.Id, Guid.NewGuid(), "other", "x", AccountFeature.None, _world.Clock.UtcNow));
        await _world.Context.SaveChangesAsync();

        var result = await NewHandler().HandleAsync(new ListSubUsersQuery(), CancellationToken.None);

        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.Total);
    }

    /// <summary>Stores a login and an open offer from the owner's account to it, listed as <paramref name="identifier"/>.</summary>
    private async Task<User> SeedOfferedLoginAsync(string username, string identifier)
    {
        var user = new User(Guid.NewGuid(), username, username + "@example.com", "hash", UserRole.Customer, SubUserWorld.Now);
        _world.Context.Users.Add(user);
        _world.Context.AccountMembershipOffers.Add(AccountMembershipOffer.Offer(
            Guid.NewGuid(), user.Id, SubUserWorld.AccountId, "acme", identifier, AccountFeature.Databases, _world.Clock.UtcNow));
        await _world.Context.SaveChangesAsync();
        return user;
    }
}
