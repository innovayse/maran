using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Queries.ListDiscoveredAccounts;
using Maran.Modules.ServerMigration.Tests.TestSupport;
using Maran.SharedKernel.Pagination;
using Maran.SharedKernel.Results;

namespace Maran.Modules.ServerMigration.Tests.Queries.ListDiscoveredAccounts;

/// <summary>
/// Listing the accounts a discovery found: only the named server's, the <c>has</c> filter, the sorts and paging with
/// the true total. The ILIKE search is a PostgreSQL function the in-memory provider cannot evaluate, so it is
/// exercised by the HTTP-level suite and not here.
/// </summary>
public sealed class ListDiscoveredAccountsQueryHandlerTests
{
    /// <summary>Builds an account with the counts the filters read.</summary>
    /// <param name="serverId">The server.</param>
    /// <param name="name">The login.</param>
    /// <param name="diskBytes">The home directory's size, or null when it could not be measured.</param>
    /// <param name="sites">How many sites.</param>
    /// <param name="databases">How many databases.</param>
    /// <param name="cron">How many scheduled jobs.</param>
    /// <param name="mailboxes">How many mailboxes.</param>
    /// <returns>The account.</returns>
    private static DiscoveredAccount Make(
        Guid serverId,
        string name,
        long? diskBytes,
        int sites = 0,
        int databases = 0,
        int cron = 0,
        int mailboxes = 0)
    {
        var account = new DiscoveredAccount(Guid.NewGuid(), serverId, name, $"/home/{name}", diskBytes, cron, mailboxes);
        for (var index = 0; index < sites; index++)
        {
            account.AddSite(
                Guid.NewGuid(), $"{name}{index}.example", [], DiscoveredSiteBackend.Static, null, $"/home/{name}/s{index}");
        }

        for (var index = 0; index < databases; index++)
        {
            account.AddDatabase(Guid.NewGuid(), $"{name}_db{index}", 1);
        }

        return account;
    }

    /// <summary>Seeds a server with four accounts and a neighbour with one.</summary>
    /// <param name="world">The test's world.</param>
    /// <returns>The server the four belong to.</returns>
    private static async Task<RemoteServer> SeedAsync(MigrationHandlerWorld world)
    {
        var server = RemoteServerTestValues.Ready();
        var neighbour = new RemoteServer(
            Guid.NewGuid(), "neighbour", "neighbour.example.net", 22, "maranmig", null, "ssh-ed25519 AAAAn", "key", RemoteServerTestValues.CreatedAt);
        await world.SeedAsync(server);
        await world.SeedAsync(neighbour);
        await world.SeedAsync(Make(server.Id, "alice", 3_000, sites: 2, databases: 1));
        await world.SeedAsync(Make(server.Id, "bob", 1_000, cron: 4));
        await world.SeedAsync(Make(server.Id, "carol", null, databases: 2, mailboxes: 7));
        await world.SeedAsync(Make(server.Id, "dave", 2_000));
        await world.SeedAsync(Make(neighbour.Id, "mallory", 9_999, sites: 1));
        return server;
    }

    /// <summary>The list holds only the named server's accounts, by name, with the true total.</summary>
    [Fact]
    public async Task The_list_holds_only_the_named_servers_accounts()
    {
        using var world = new MigrationHandlerWorld();
        var server = await SeedAsync(world);

        var result = await world.ListAccounts.HandleAsync(new ListDiscoveredAccountsQuery(ServerId: server.Id), CancellationToken.None);

        Assert.Equal(4, result.Value.Total);
        Assert.Equal(["alice", "bob", "carol", "dave"], result.Value.Items.Select(row =>
        {
            return row.Name;
        }));
        Assert.DoesNotContain(result.Value.Items, row =>
        {
            return row.Name == "mallory";
        });
        var alice = result.Value.Items[0];
        Assert.Equal(["alice0.example", "alice1.example"], alice.Domains);
        Assert.Equal(2, alice.SiteCount);
        Assert.Equal(1, alice.DatabaseCount);
    }

    /// <summary>An unknown server is not found, not an empty page that would look like a server with no accounts.</summary>
    [Fact]
    public async Task An_unknown_server_is_not_found()
    {
        using var world = new MigrationHandlerWorld();
        await SeedAsync(world);

        var result = await world.ListAccounts.HandleAsync(
            new ListDiscoveredAccountsQuery(ServerId: Guid.NewGuid()), CancellationToken.None);

        Assert.Equal("RemoteServerNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    /// <summary>The <c>has</c> filter keeps the accounts with at least one of the named thing; an unknown word keeps none.</summary>
    /// <param name="has">The filter value.</param>
    /// <param name="expected">The accounts that remain.</param>
    [Theory]
    [InlineData("sites", new[] { "alice" })]
    [InlineData("databases", new[] { "alice", "carol" })]
    [InlineData("cron", new[] { "bob" })]
    [InlineData("mail", new[] { "carol" })]
    [InlineData("Sites", new string[0])]
    [InlineData("everything", new string[0])]
    public async Task The_has_filter_keeps_the_accounts_that_have_the_thing(string has, string[] expected)
    {
        using var world = new MigrationHandlerWorld();
        var server = await SeedAsync(world);

        var result = await world.ListAccounts.HandleAsync(
            new ListDiscoveredAccountsQuery(Has: FilterValue.Of(has), ServerId: server.Id), CancellationToken.None);

        Assert.Equal(expected, result.Value.Items.Select(row =>
        {
            return row.Name;
        }));
        Assert.Equal(expected.Length, result.Value.Total);
    }

    /// <summary>The sorts order by disk and by counts, in both directions.</summary>
    [Fact]
    public async Task The_sorts_order_by_disk_and_by_counts()
    {
        using var world = new MigrationHandlerWorld();
        var server = await SeedAsync(world);

        var byDisk = await world.ListAccounts.HandleAsync(
            new ListDiscoveredAccountsQuery(Sort: SortRequest.Parse("-disk"), ServerId: server.Id), CancellationToken.None);
        var bySites = await world.ListAccounts.HandleAsync(
            new ListDiscoveredAccountsQuery(Sort: SortRequest.Parse("-sites"), ServerId: server.Id), CancellationToken.None);
        var byDatabases = await world.ListAccounts.HandleAsync(
            new ListDiscoveredAccountsQuery(Sort: SortRequest.Parse("-databases"), ServerId: server.Id), CancellationToken.None);

        Assert.Equal(["alice", "dave", "bob", "carol"], byDisk.Value.Items.Select(row =>
        {
            return row.Name;
        }));
        Assert.Equal("alice", bySites.Value.Items[0].Name);
        Assert.Equal("carol", byDatabases.Value.Items[0].Name);
        Assert.False(byDisk.Value.Items[3].DiskKnown);
    }

    /// <summary>A page answers its rows with the true total; a page past the end is empty with that total.</summary>
    [Fact]
    public async Task A_page_carries_the_true_total()
    {
        using var world = new MigrationHandlerWorld();
        var server = await SeedAsync(world);

        // The smallest size the list offers is ten, so a second page needs eleven accounts: the four seeded and seven more.
        for (var index = 0; index < 7; index++)
        {
            await world.SeedAsync(Make(server.Id, $"erin{index}", 500));
        }

        var first = await world.ListAccounts.HandleAsync(
            new ListDiscoveredAccountsQuery(Page: 1, PageSize: 10, ServerId: server.Id), CancellationToken.None);
        var second = await world.ListAccounts.HandleAsync(
            new ListDiscoveredAccountsQuery(Page: 2, PageSize: 10, ServerId: server.Id), CancellationToken.None);
        var past = await world.ListAccounts.HandleAsync(
            new ListDiscoveredAccountsQuery(Page: 5, PageSize: 10, ServerId: server.Id), CancellationToken.None);

        Assert.Equal(10, first.Value.Items.Count);
        Assert.Equal("erin6", Assert.Single(second.Value.Items).Name);
        Assert.Equal(11, second.Value.Total);
        Assert.Empty(past.Value.Items);
        Assert.Equal(11, past.Value.Total);
    }
}
