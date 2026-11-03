using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Queries.ListSubUsers;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
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
        // 12 rows against the smallest allowed page size (10), so the second page is a real boundary.
        foreach (var name in new[] { "a", "b", "c", "d", "e", "f" })
        {
            await _world.SeedMemberAsync(name, AccountFeature.None);
        }

        foreach (var name in new[] { "g", "h", "i", "j", "k", "l" })
        {
            await SeedOfferedLoginAsync(name, name);
        }

        var first = await NewHandler().HandleAsync(new ListSubUsersQuery(Page: 1, PageSize: 10), CancellationToken.None);
        var second = await NewHandler().HandleAsync(new ListSubUsersQuery(Page: 2, PageSize: 10), CancellationToken.None);

        Assert.Equal(12, first.Value.Total);
        Assert.Equal(10, first.Value.Items.Count);
        Assert.Equal(2, second.Value.Items.Count);
        Assert.Equal(
            12,
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
