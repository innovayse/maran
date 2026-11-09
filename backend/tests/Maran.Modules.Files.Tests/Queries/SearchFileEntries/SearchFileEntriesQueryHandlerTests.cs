using Maran.Agent.Client.Services.FilesService;
using Maran.Modules.Files.Queries.SearchFileEntries;
using Maran.Modules.Files.Tests.TestSupport;
using Maran.SharedKernel.Pagination;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Files.Tests.Queries.SearchFileEntries;

/// <summary>What searching below a folder asks of the agent, who may ask it, and what comes back.</summary>
public sealed class SearchFileEntriesQueryHandlerTests
{
    /// <summary>Builds the handler over a world.</summary>
    /// <param name="world">The world the handler reads.</param>
    /// <returns>The handler.</returns>
    private static SearchFileEntriesQueryHandler HandlerOver(FilesWorld world)
    {
        return new SearchFileEntriesQueryHandler(
            world.Gate, world.Names, world.Agent, NullLogger<SearchFileEntriesQueryHandler>.Instance);
    }

    /// <summary>A query as the action stamps it.</summary>
    /// <param name="accountId">The account asked about.</param>
    /// <param name="sort">The sort key, if any.</param>
    /// <returns>The query.</returns>
    private static SearchFileEntriesQuery Query(Guid accountId, string sort = "")
    {
        return new SearchFileEntriesQuery(
            accountId,
            "docs",
            false,
            1,
            10,
            SearchTerm.Of("report"),
            SortRequest.Parse(sort),
            FilesWorld.Origin.IpAddress,
            FilesWorld.Origin.UserAgent);
    }

    /// <summary>Another tenant's account is not found and the agent never searches.</summary>
    [Fact]
    public async Task Another_tenants_account_is_not_found_and_the_agent_sees_nothing()
    {
        var world = new FilesWorld();

        var result = await HandlerOver(world).HandleAsync(Query(FilesWorld.StrangerAccountId), default);

        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Empty(world.Agent.Calls);
    }

    /// <summary>The control: the caller's own account is searched, to the agent's deepest level.</summary>
    [Fact]
    public async Task The_callers_own_account_is_searched_to_the_agents_deepest_level()
    {
        var world = new FilesWorld();

        var result = await HandlerOver(world).HandleAsync(Query(FilesWorld.OwnAccountId), default);

        Assert.True(result.IsSuccess);
        var search = Assert.Single(world.Agent.SearchRequests);
        Assert.Equal(new AgentSearchRequest("docs", "report", 16u, 500u, false), search);
    }

    /// <summary>When a bound stopped the agent the answer says so and is still a success.</summary>
    [Fact]
    public async Task A_search_the_agent_cut_short_says_so()
    {
        var world = new FilesWorld();
        world.Agent.SearchTruncated = true;
        world.Agent.SearchMatches.Add(new FileSearchMatchDto("docs/report.txt", FilesWorld.File("report.txt")));

        var result = await HandlerOver(world).HandleAsync(Query(FilesWorld.OwnAccountId), default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Truncated);
        Assert.Equal("docs/report.txt", Assert.Single(result.Value.Items).Path);
    }

    /// <summary>The matches are ordered here by the sort asked for, descending size first.</summary>
    [Fact]
    public async Task The_matches_are_ordered_by_the_sort_asked_for()
    {
        var world = new FilesWorld();
        world.Agent.SearchMatches.Add(new FileSearchMatchDto("docs/report-small.txt", FilesWorld.File("report-small.txt", 1)));
        world.Agent.SearchMatches.Add(new FileSearchMatchDto("docs/a/report-big.txt", FilesWorld.File("report-big.txt", 900)));

        var result = await HandlerOver(world).HandleAsync(Query(FilesWorld.OwnAccountId, "-size"), default);

        Assert.Equal(
            ["docs/a/report-big.txt", "docs/report-small.txt"],
            result.Value.Items.Select(entry => { return entry.Path; }));
    }

    /// <summary>A customer on a suspended account is refused before the agent searches.</summary>
    [Fact]
    public async Task A_suspended_account_is_refused_to_its_customer()
    {
        var world = new FilesWorld(Maran.Sdk.Contracts.AccountState.Suspended);

        var result = await HandlerOver(world).HandleAsync(Query(FilesWorld.OwnAccountId), default);

        Assert.Equal("FilesAccountSuspended", result.Error!.Code);
        Assert.Empty(world.Agent.Calls);
    }
}
