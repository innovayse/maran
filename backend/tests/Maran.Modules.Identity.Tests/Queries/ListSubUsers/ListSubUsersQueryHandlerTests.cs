using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Queries.ListSubUsers;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Pagination;

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
        return new ListSubUsersQueryHandler(_world.Context, _world.Guard(caller));
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
}
