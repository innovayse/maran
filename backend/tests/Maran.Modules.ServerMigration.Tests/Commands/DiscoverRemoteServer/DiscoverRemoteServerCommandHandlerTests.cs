using Maran.Agent.Client.Services.TransferService;
using Maran.Modules.ServerMigration.Commands.DiscoverRemoteServer;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Domain.Policies;
using Maran.Modules.ServerMigration.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.ServerMigration.Tests.Commands.DiscoverRemoteServer;

/// <summary>
/// Discovering a remote server: the stream is stored whole and only if it finished, the old discovery is replaced and
/// never mixed with the new one, a server that is not ready is not read, and a failure leaves the previous discovery.
/// Each refusal has an accepting control.
/// </summary>
public sealed class DiscoverRemoteServerCommandHandlerTests
{
    /// <summary>A discovery of the server the tests seed.</summary>
    /// <param name="serverId">The server.</param>
    /// <returns>The command, stamped as the action would.</returns>
    private static DiscoverRemoteServerCommand Command(Guid serverId)
    {
        return new DiscoverRemoteServerCommand(serverId, "203.0.113.5", "tests");
    }

    /// <summary>An account the source reports.</summary>
    /// <param name="name">The login.</param>
    /// <param name="diskBytes">The home directory's size, or null when it could not be measured.</param>
    /// <returns>The account, with one site and one database.</returns>
    private static AgentSourceAccount Account(string name, long? diskBytes = 1_000)
    {
        return new AgentSourceAccount(
            name,
            $"/home/{name}",
            diskBytes,
            [new AgentSourceSite($"{name}.example", [$"www.{name}.example"], SourceSiteBackend.Php, "8.2", $"/home/{name}/public_html")],
            [new AgentSourceDatabase($"{name}_db", 2_048)],
            3,
            4);
    }

    /// <summary>A stream that reports the accounts and the warnings and then finishes.</summary>
    /// <param name="accounts">The accounts.</param>
    /// <param name="warnings">The warnings.</param>
    /// <returns>The events.</returns>
    private static List<AgentSourceInventoryEvent> Stream(
        List<AgentSourceAccount> accounts,
        params AgentSourceWarning[] warnings)
    {
        var events = new List<AgentSourceInventoryEvent> { new AgentSourceInventoryProgress(SourceInventoryStep.Accounts) };
        events.AddRange(accounts.Select(account =>
        {
            return (AgentSourceInventoryEvent)new AgentSourceInventoryAccountFound(account);
        }));
        events.AddRange(warnings.Select(warning =>
        {
            return (AgentSourceInventoryEvent)new AgentSourceInventoryWarningRaised(warning);
        }));
        events.Add(new AgentSourceInventoryDone(accounts.Count, warnings.Length));
        return events;
    }

    /// <summary>A finished stream is stored whole: accounts with their sites and databases, the warnings, and the count on the server.</summary>
    [Fact]
    public async Task A_finished_stream_is_stored_whole()
    {
        using var world = new MigrationHandlerWorld();
        var server = RemoteServerTestValues.Ready();
        await world.SeedAsync(server);
        world.Agent.Inventory = Stream(
            [Account("alice"), Account("bob", diskBytes: null)],
            new AgentSourceWarning(SourceWarningKind.MailNotMigrated, "alice"));

        var result = await world.Discover.HandleAsync(Command(server.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.AccountCount);
        Assert.Equal(world.Clock.UtcNow, result.Value.DiscoveredAt);
        var warning = Assert.Single(result.Value.Warnings);
        Assert.Equal(DiscoveryWarningKind.MailNotMigrated, warning.Kind);
        Assert.Equal("alice", warning.Subject);
        Assert.Equal("DiscoveryWarningMailNotMigrated", warning.Message);

        var accounts = await world.Reader.DiscoveredAccounts.AsNoTracking()
            .Include(row => row.Sites).Include(row => row.Databases).OrderBy(row => row.Name).ToListAsync();
        Assert.Equal(["alice", "bob"], accounts.Select(row =>
        {
            return row.Name;
        }));
        Assert.True(accounts[0].DiskKnown);
        Assert.False(accounts[1].DiskKnown);
        Assert.Equal(1, accounts[0].SiteCount);
        Assert.Equal(1, accounts[0].DatabaseCount);
        Assert.Equal("alice.example www.alice.example", accounts[0].DomainsText);
        Assert.Equal(DiscoveredSiteBackend.Php, Assert.Single(accounts[0].Sites).Backend);
        Assert.Equal(2_048, Assert.Single(accounts[0].Databases).SizeBytes);
        var stored = await world.Reader.RemoteServers.AsNoTracking().SingleAsync();
        Assert.Equal(2, stored.AccountsFound);
        Assert.Equal(world.Clock.UtcNow, stored.DiscoveredAt);
        Assert.Single(await world.Reader.DiscoveryWarnings.AsNoTracking().ToListAsync());

        var entry = Assert.Single(world.Entries);
        Assert.Equal(MigrationAuditActions.RemoteServerDiscovered, entry.Action);
        Assert.True(entry.Succeeded);
        var (endpoint, credential) = Assert.Single(world.Agent.InventoryCalls);
        Assert.Equal(server.Host, endpoint.Host);
        Assert.Equal(server.HostKey, credential.HostKey);
    }

    /// <summary>A second discovery replaces the first: nothing of the old one survives beside the new one.</summary>
    [Fact]
    public async Task A_second_discovery_replaces_the_first()
    {
        using var world = new MigrationHandlerWorld();
        var server = RemoteServerTestValues.Ready();
        await world.SeedAsync(server);
        world.Agent.Inventory = Stream([Account("alice"), Account("bob")], new AgentSourceWarning(SourceWarningKind.Truncated, "accounts"));
        await world.Discover.HandleAsync(Command(server.Id), CancellationToken.None);
        world.Agent.Inventory = Stream([Account("alice", diskBytes: 9)]);

        var second = await world.Discover.HandleAsync(Command(server.Id), CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.Equal(1, second.Value.AccountCount);
        Assert.Empty(second.Value.Warnings);
        var account = await world.Reader.DiscoveredAccounts.AsNoTracking().SingleAsync();
        Assert.Equal("alice", account.Name);
        Assert.Equal(9, account.DiskBytes);
        Assert.Single(await world.Reader.DiscoveredSites.AsNoTracking().ToListAsync());
        Assert.Single(await world.Reader.DiscoveredDatabases.AsNoTracking().ToListAsync());
        Assert.Empty(await world.Reader.DiscoveryWarnings.AsNoTracking().ToListAsync());
        Assert.Equal(1, (await world.Reader.RemoteServers.AsNoTracking().SingleAsync()).AccountsFound);
    }

    /// <summary>A stream that fails half way stores nothing and leaves the previous discovery; the finished stream is the accepting control.</summary>
    [Fact]
    public async Task A_stream_that_fails_leaves_the_previous_discovery()
    {
        using var world = new MigrationHandlerWorld();
        var server = RemoteServerTestValues.Ready();
        await world.SeedAsync(server);
        world.Agent.Inventory = Stream([Account("alice")]);
        await world.Discover.HandleAsync(Command(server.Id), CancellationToken.None);
        world.Agent.Inventory =
        [
            new AgentSourceInventoryAccountFound(Account("carol")),
            new AgentSourceInventoryFailed(Error.Of("AgentSystemFailure", ErrorType.Failure), SourceFailureReason.Unspecified),
        ];

        var refused = await world.Discover.HandleAsync(Command(server.Id), CancellationToken.None);

        Assert.Equal("MigrationDiscoveryFailed", refused.Error!.Code);
        Assert.Equal(ErrorType.Failure, refused.Error.Type);
        Assert.Equal("alice", (await world.Reader.DiscoveredAccounts.AsNoTracking().SingleAsync()).Name);
        Assert.False(world.Entries[^1].Succeeded);
        Assert.Equal(RemoteServerState.Ready, (await world.Reader.RemoteServers.AsNoTracking().SingleAsync()).State);
    }

    /// <summary>A stream that stops without stating an ending is a failure and never a completion.</summary>
    [Fact]
    public async Task A_stream_with_no_ending_is_a_failure()
    {
        using var world = new MigrationHandlerWorld();
        var server = RemoteServerTestValues.Ready();
        await world.SeedAsync(server);
        world.Agent.Inventory = [new AgentSourceInventoryAccountFound(Account("alice"))];

        var refused = await world.Discover.HandleAsync(Command(server.Id), CancellationToken.None);

        Assert.Equal("MigrationDiscoveryFailed", refused.Error!.Code);
        Assert.Empty(await world.Reader.DiscoveredAccounts.AsNoTracking().ToListAsync());
    }

    /// <summary>A refusal that describes the server is written to its row; the next attempt then needs a decision.</summary>
    [Fact]
    public async Task A_changed_host_key_during_discovery_is_recorded_on_the_server()
    {
        using var world = new MigrationHandlerWorld();
        var server = RemoteServerTestValues.Ready();
        await world.SeedAsync(server);
        world.Agent.Inventory =
        [
            new AgentSourceInventoryFailed(Error.Of("AgentSystemFailure", ErrorType.Failure), SourceFailureReason.HostKeyChanged),
        ];

        var refused = await world.Discover.HandleAsync(Command(server.Id), CancellationToken.None);
        var again = await world.Discover.HandleAsync(Command(server.Id), CancellationToken.None);

        Assert.Equal("RemoteServerHostKeyChanged", refused.Error!.Code);
        Assert.Equal(ErrorType.Conflict, refused.Error.Type);
        var stored = await world.Reader.RemoteServers.AsNoTracking().SingleAsync();
        Assert.Equal(RemoteServerState.HostKeyChanged, stored.State);
        Assert.Equal(RemoteFailureKind.HostKeyChanged, stored.LastFailure);
        Assert.Equal("RemoteServerNotReady", again.Error!.Code);
        Assert.Single(world.Agent.InventoryCalls);
    }

    /// <summary>A server with no pinned key, or one in a failed state, is not read; a pinned one is.</summary>
    [Fact]
    public async Task A_server_that_is_not_ready_is_not_read()
    {
        using var world = new MigrationHandlerWorld();
        var unconfirmed = RemoteServerTestValues.Scanned();
        await world.SeedAsync(unconfirmed);

        var refused = await world.Discover.HandleAsync(Command(unconfirmed.Id), CancellationToken.None);

        Assert.Equal("RemoteServerNotReady", refused.Error!.Code);
        Assert.Equal(ErrorType.Conflict, refused.Error.Type);
        Assert.Empty(world.Agent.InventoryCalls);

        var pinned = RemoteServerTestValues.Pinned();
        await world.SeedAsync(pinned);
        var accepted = await world.Discover.HandleAsync(Command(pinned.Id), CancellationToken.None);
        Assert.True(accepted.IsSuccess);
        Assert.Single(world.Agent.InventoryCalls);
    }

    /// <summary>A source that reports more accounts than the panel will hold is refused whole; one at the bound is stored.</summary>
    [Fact]
    public async Task A_source_that_reports_more_accounts_than_the_bound_is_refused_whole()
    {
        using var world = new MigrationHandlerWorld();
        var server = RemoteServerTestValues.Ready();
        await world.SeedAsync(server);
        var atTheBound = Enumerable.Range(0, DiscoveryLimits.MaxAccounts).Select(index =>
        {
            return new AgentSourceAccount($"u{index}", $"/home/u{index}", 1, [], [], 0, 0);
        }).ToList();
        world.Agent.Inventory = Stream(atTheBound);

        var accepted = await world.Discover.HandleAsync(Command(server.Id), CancellationToken.None);

        Assert.True(accepted.IsSuccess);
        Assert.Equal(DiscoveryLimits.MaxAccounts, accepted.Value.AccountCount);

        world.Agent.Inventory = Stream(
            atTheBound.Append(new AgentSourceAccount("one-too-many", "/home/x", 1, [], [], 0, 0)).ToList());
        var refused = await world.Discover.HandleAsync(Command(server.Id), CancellationToken.None);

        Assert.Equal("MigrationDiscoveryFailed", refused.Error!.Code);
        Assert.Equal(DiscoveryLimits.MaxAccounts, await world.Reader.DiscoveredAccounts.AsNoTracking().CountAsync());
    }

    /// <summary>A login the source names twice is stored once; the count is the accounts stored.</summary>
    [Fact]
    public async Task A_login_named_twice_is_stored_once()
    {
        using var world = new MigrationHandlerWorld();
        var server = RemoteServerTestValues.Ready();
        await world.SeedAsync(server);
        world.Agent.Inventory = Stream([Account("alice"), Account("alice"), Account("bob")]);

        var result = await world.Discover.HandleAsync(Command(server.Id), CancellationToken.None);

        Assert.Equal(2, result.Value.AccountCount);
        Assert.Equal(2, await world.Reader.DiscoveredAccounts.AsNoTracking().CountAsync());
    }

    /// <summary>An unknown server is not found and the agent is not asked.</summary>
    [Fact]
    public async Task An_unknown_server_is_not_found()
    {
        using var world = new MigrationHandlerWorld();

        var result = await world.Discover.HandleAsync(Command(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal("RemoteServerNotFound", result.Error!.Code);
        Assert.Empty(world.Agent.InventoryCalls);
    }
}
