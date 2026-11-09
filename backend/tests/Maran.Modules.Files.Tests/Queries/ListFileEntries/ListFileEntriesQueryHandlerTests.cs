using Maran.Agent.Client.Services.FilesService;
using Maran.Modules.Files.Queries.ListFileEntries;
using Maran.Modules.Files.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Pagination;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Files.Tests.Queries.ListFileEntries;

/// <summary>What listing a folder asks of the agent, who may ask it, and what comes back.</summary>
public sealed class ListFileEntriesQueryHandlerTests
{
    /// <summary>Builds the handler over a world.</summary>
    /// <param name="world">The world the handler reads.</param>
    /// <returns>The handler.</returns>
    private static ListFileEntriesQueryHandler HandlerOver(FilesWorld world)
    {
        return new ListFileEntriesQueryHandler(
            world.Gate, world.Names, world.Agent, NullLogger<ListFileEntriesQueryHandler>.Instance);
    }

    /// <summary>A query as the action stamps it.</summary>
    /// <param name="accountId">The account asked about.</param>
    /// <param name="path">The folder.</param>
    /// <param name="showHidden">Whether hidden entries are listed.</param>
    /// <param name="page">The page.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="search">The search text, if any.</param>
    /// <param name="sort">The sort key, if any.</param>
    /// <returns>The query.</returns>
    private static ListFileEntriesQuery Query(
        Guid accountId,
        string path = "",
        bool showHidden = false,
        int page = 1,
        int pageSize = 10,
        string search = "",
        string sort = "")
    {
        return new ListFileEntriesQuery(
            accountId,
            path,
            showHidden,
            page,
            pageSize,
            search.Length == 0 ? SearchTerm.None : SearchTerm.Of(search),
            SortRequest.Parse(sort),
            FilesWorld.Origin.IpAddress,
            FilesWorld.Origin.UserAgent);
    }

    /// <summary>Another tenant's account is not found, the agent is never asked, and the probe is journalled.</summary>
    [Fact]
    public async Task Another_tenants_account_is_not_found_and_the_agent_sees_nothing()
    {
        var world = new FilesWorld();
        world.Agent.Put(string.Empty, FilesWorld.File("a.txt"));

        var result = await HandlerOver(world).HandleAsync(Query(FilesWorld.StrangerAccountId), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(world.Agent.Calls);
        var entry = Assert.Single(world.Writer.Entries);
        Assert.Equal(FileAuditActions.FileAccessRefused, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>The control for the test above: the caller's own account is answered, and reaches the agent.</summary>
    [Fact]
    public async Task The_callers_own_account_is_listed_and_reaches_the_agent()
    {
        var world = new FilesWorld();
        world.Agent.Put(string.Empty, FilesWorld.File("a.txt"));

        var result = await HandlerOver(world).HandleAsync(Query(FilesWorld.OwnAccountId), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("a.txt", Assert.Single(result.Value.Items).Name);
        Assert.Single(world.Agent.Calls);
        Assert.Empty(world.Writer.Entries);
    }

    /// <summary>A customer on a suspended account is refused before the agent is asked; an administrator is not.</summary>
    [Fact]
    public async Task A_suspended_account_is_refused_to_its_customer_and_open_to_an_administrator()
    {
        var customerWorld = new FilesWorld(AccountState.Suspended);
        customerWorld.Agent.Put(string.Empty, FilesWorld.File("a.txt"));
        var administratorWorld = new FilesWorld(AccountState.Suspended, administrator: true);
        administratorWorld.Agent.Put(string.Empty, FilesWorld.File("a.txt"));

        var refused = await HandlerOver(customerWorld).HandleAsync(Query(FilesWorld.OwnAccountId), default);
        var allowed = await HandlerOver(administratorWorld).HandleAsync(Query(FilesWorld.OwnAccountId), default);

        Assert.Equal("FilesAccountSuspended", refused.Error!.Code);
        Assert.Empty(customerWorld.Agent.Calls);
        Assert.True(allowed.IsSuccess);
    }

    /// <summary>The agent is asked for exactly the page, order and hidden setting requested, and its total is the page's total.</summary>
    [Fact]
    public async Task The_agent_is_asked_for_the_page_the_order_and_the_hidden_setting_requested()
    {
        var world = new FilesWorld();
        for (var index = 0; index < 30; index++)
        {
            world.Agent.Put("docs", FilesWorld.File($"file{index:00}.txt"));
        }

        var result = await HandlerOver(world).HandleAsync(
            Query(FilesWorld.OwnAccountId, "docs", showHidden: true, page: 2, pageSize: 10, sort: "-modified"), default);

        var listing = Assert.Single(world.Agent.ListRequests);
        Assert.Equal(new AgentListRequest("docs", 10u, 10u, true, DirectorySortOrder.Modified, true), listing);
        Assert.Equal(30, result.Value.Total);
        Assert.Equal(2, result.Value.Page);
        Assert.Equal(10, result.Value.Items.Count);
        Assert.All(result.Value.Items, entry => { Assert.StartsWith("docs/file", entry.Path); });
    }

    /// <summary>With no sort named the listing is by name, ascending, as the sort map's default says.</summary>
    [Fact]
    public async Task The_default_order_is_by_name_ascending()
    {
        var world = new FilesWorld();
        world.Agent.Put(string.Empty, FilesWorld.File("b.txt"));

        await HandlerOver(world).HandleAsync(Query(FilesWorld.OwnAccountId), default);

        var listing = Assert.Single(world.Agent.ListRequests);
        Assert.Equal(DirectorySortOrder.Name, listing.Sort);
        Assert.False(listing.Descending);
        Assert.False(listing.IncludeHidden);
    }

    /// <summary>A folder comes before a file whatever the order, and a hidden entry is only listed when asked for.</summary>
    [Fact]
    public async Task Folders_come_first_and_hidden_entries_only_when_asked_for()
    {
        var world = new FilesWorld();
        world.Agent.Put(string.Empty, FilesWorld.File("a.txt"));
        world.Agent.Put(string.Empty, FilesWorld.Folder("zeta"));
        world.Agent.Put(string.Empty, FilesWorld.File(".secret"));

        var hidden = await HandlerOver(world).HandleAsync(Query(FilesWorld.OwnAccountId), default);
        var shown = await HandlerOver(world).HandleAsync(Query(FilesWorld.OwnAccountId, showHidden: true), default);

        Assert.Equal(["zeta", "a.txt"], hidden.Value.Items.Select(entry => { return entry.Name; }));
        Assert.Equal(["zeta", ".secret", "a.txt"], shown.Value.Items.Select(entry => { return entry.Name; }));
    }

    /// <summary>A search of one folder asks the agent for its direct entries only and pages the matches here.</summary>
    [Fact]
    public async Task A_search_asks_for_the_direct_entries_only_and_pages_the_matches()
    {
        var world = new FilesWorld();
        world.Agent.SearchMatches.Add(new FileSearchMatchDto("docs/a.txt", FilesWorld.File("a.txt")));
        world.Agent.SearchMatches.Add(new FileSearchMatchDto("docs/b.txt", FilesWorld.File("b.txt")));
        world.Agent.SearchMatches.Add(new FileSearchMatchDto("docs/c.log", FilesWorld.File("c.log")));

        var result = await HandlerOver(world).HandleAsync(
            Query(FilesWorld.OwnAccountId, "docs", search: "txt", pageSize: 10), default);

        var search = Assert.Single(world.Agent.SearchRequests);
        Assert.Equal(new AgentSearchRequest("docs", "txt", 1u, 500u, false), search);
        Assert.Empty(world.Agent.ListRequests);
        Assert.Equal(["a.txt", "b.txt"], result.Value.Items.Select(entry => { return entry.Name; }));
        Assert.Equal(2, result.Value.Total);
    }

    /// <summary>A folder that is not there is the module's not-found, not the agent's own code.</summary>
    [Fact]
    public async Task A_missing_folder_is_translated_to_the_modules_not_found()
    {
        var world = new FilesWorld();

        var result = await HandlerOver(world).HandleAsync(Query(FilesWorld.OwnAccountId, "nope"), default);

        Assert.Equal("FilesEntryNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    /// <summary>
    /// A hostile path is stopped by the validator and the agent sees nothing, which is the order the bus
    /// runs them in; the control is a legal path, which gets through the same two steps to the agent.
    /// </summary>
    /// <param name="path">A hostile path.</param>
    [Theory]
    [MemberData(nameof(FilesHostilePaths.Refused), MemberType = typeof(FilesHostilePaths))]
    public async Task A_hostile_path_stopped_by_the_validator_never_reaches_the_agent(string path)
    {
        var world = new FilesWorld();
        world.Agent.Put(FilesHostilePaths.Legal, FilesWorld.File("a.txt"));
        var hostile = Query(FilesWorld.OwnAccountId, path);
        var legal = Query(FilesWorld.OwnAccountId, FilesHostilePaths.Legal);
        var validator = new ListFileEntriesQueryValidator();

        if (validator.Validate(hostile).IsValid)
        {
            await HandlerOver(world).HandleAsync(hostile, default);
        }

        Assert.Empty(world.Agent.Calls);

        Assert.True(validator.Validate(legal).IsValid);
        await HandlerOver(world).HandleAsync(legal, default);
        Assert.Single(world.Agent.Calls);
    }
}
