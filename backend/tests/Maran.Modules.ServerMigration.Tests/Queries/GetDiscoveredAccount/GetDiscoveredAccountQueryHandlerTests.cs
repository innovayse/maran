using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Queries.GetDiscoveredAccount;
using Maran.Modules.ServerMigration.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.ServerMigration.Tests.Queries.GetDiscoveredAccount;

/// <summary>Reading one discovered account: its sites and databases, and no way to reach it through another server.</summary>
public sealed class GetDiscoveredAccountQueryHandlerTests
{
    /// <summary>The detail carries the account's sites, aliases, backends and databases.</summary>
    [Fact]
    public async Task The_detail_carries_the_sites_and_databases()
    {
        using var world = new MigrationHandlerWorld();
        var server = RemoteServerTestValues.Ready();
        await world.SeedAsync(server);
        var account = new DiscoveredAccount(Guid.NewGuid(), server.Id, "alice", "/home/alice", 3_000, 2, 5);
        account.AddSite(Guid.NewGuid(), "alice.example", ["www.alice.example", "shop.alice.example"], DiscoveredSiteBackend.Php, "8.2", "/home/alice/public_html");
        account.AddSite(Guid.NewGuid(), "static.example", [], DiscoveredSiteBackend.Static, null, "/home/alice/static");
        account.AddDatabase(Guid.NewGuid(), "alice_wp", 4_096);
        account.AddDatabase(Guid.NewGuid(), "alice_old", null);
        await world.SeedAsync(account);

        var result = await world.GetAccount.HandleAsync(new GetDiscoveredAccountQuery(server.Id, account.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("alice", result.Value.Name);
        Assert.Equal(5, result.Value.MailboxCount);
        Assert.Equal(2, result.Value.SiteCount);
        var php = result.Value.Sites.Single(site =>
        {
            return site.Domain == "alice.example";
        });
        Assert.Equal(["www.alice.example", "shop.alice.example"], php.Aliases);
        Assert.Equal(DiscoveredSiteBackend.Php, php.Backend);
        Assert.Equal("DiscoveredSiteBackendPhp", php.BackendName);
        Assert.Equal("8.2", php.PhpVersion);
        Assert.Null(result.Value.Sites.Single(site =>
        {
            return site.Domain == "static.example";
        }).PhpVersion);
        Assert.Equal(["alice_old", "alice_wp"], result.Value.Databases.Select(database =>
        {
            return database.Name;
        }));
        Assert.False(result.Value.Databases[0].SizeKnown);
        Assert.True(result.Value.Databases[1].SizeKnown);
    }

    /// <summary>An account is not reachable through another server's identifier; through its own it is.</summary>
    [Fact]
    public async Task An_account_is_not_reachable_through_another_server()
    {
        using var world = new MigrationHandlerWorld();
        var server = RemoteServerTestValues.Ready();
        var neighbour = new RemoteServer(
            Guid.NewGuid(), "neighbour", "neighbour.example.net", 22, "maranmig", null, "ssh-ed25519 AAAAn", "key", RemoteServerTestValues.CreatedAt);
        await world.SeedAsync(server);
        await world.SeedAsync(neighbour);
        var account = new DiscoveredAccount(Guid.NewGuid(), server.Id, "alice", "/home/alice", 3_000, 0, 0);
        await world.SeedAsync(account);

        var crossed = await world.GetAccount.HandleAsync(new GetDiscoveredAccountQuery(neighbour.Id, account.Id), CancellationToken.None);
        var own = await world.GetAccount.HandleAsync(new GetDiscoveredAccountQuery(server.Id, account.Id), CancellationToken.None);
        var unknown = await world.GetAccount.HandleAsync(new GetDiscoveredAccountQuery(server.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal("RemoteServerNotFound", crossed.Error!.Code);
        Assert.Equal(ErrorType.NotFound, crossed.Error.Type);
        Assert.True(own.IsSuccess);
        Assert.Equal(ErrorType.NotFound, unknown.Error!.Type);
    }
}
