using Maran.Modules.ServerMigration.Commands.EditRemoteServer;
using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.ServerMigration.Tests.Commands.EditRemoteServer;

/// <summary>
/// Editing a remote server: a new label keeps the confirmed host key, a new address, port or login withdraws it and
/// the discovery of the old target, the registry's uniqueness holds, and every outcome is journalled.
/// </summary>
public sealed class EditRemoteServerCommandHandlerTests
{
    /// <summary>An edit of the server the tests seed.</summary>
    /// <param name="serverId">The server.</param>
    /// <param name="name">The label.</param>
    /// <param name="host">The address.</param>
    /// <param name="port">The port.</param>
    /// <param name="sshUser">The login.</param>
    /// <param name="allowedFrom">The key restriction, or null.</param>
    /// <returns>The command, stamped as the action would.</returns>
    private static EditRemoteServerCommand Command(
        Guid serverId,
        string name = "old-host",
        string host = "old.example.net",
        int port = 22,
        string sshUser = "maranmig",
        string? allowedFrom = null)
    {
        return new EditRemoteServerCommand(name, host, port, sshUser, allowedFrom, serverId, "203.0.113.5", "tests");
    }

    /// <summary>A new label, or a new key restriction, keeps the pin, the observations and the discovery.</summary>
    [Fact]
    public async Task A_new_label_keeps_the_pin_and_the_discovery()
    {
        using var world = new MigrationHandlerWorld();
        var server = RemoteServerTestValues.Ready();
        await world.SeedAsync(server);
        await world.SeedAsync(new DiscoveredAccount(Guid.NewGuid(), server.Id, "alice", "/home/alice", 10, 0, 0));

        var result = await world.Edit.HandleAsync(
            Command(server.Id, "renamed", allowedFrom: "10.0.0.1"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("renamed", result.Value.Name);
        Assert.Equal("10.0.0.1/32", result.Value.AllowedFrom);
        Assert.Equal(RemoteServerState.Ready, result.Value.State);
        Assert.Equal(RemoteServerTestValues.FingerprintA, result.Value.HostKeyFingerprint);
        Assert.Single(await world.Reader.DiscoveredAccounts.AsNoTracking().ToListAsync());
        var entry = Assert.Single(world.Entries);
        Assert.Equal(MigrationAuditActions.RemoteServerEdited, entry.Action);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A new address withdraws the pin and what a discovery of the old target found; the label alone did not.</summary>
    [Fact]
    public async Task A_new_address_withdraws_the_pin_and_the_discovery()
    {
        using var world = new MigrationHandlerWorld();
        var server = RemoteServerTestValues.Ready();
        await world.SeedAsync(server);
        var account = new DiscoveredAccount(Guid.NewGuid(), server.Id, "alice", "/home/alice", 10, 0, 0);
        account.AddSite(Guid.NewGuid(), "alice.example", [], DiscoveredSiteBackend.Php, "8.2", "/home/alice/public_html");
        account.AddDatabase(Guid.NewGuid(), "alice_db", 5);
        await world.SeedAsync(account);
        await world.SeedAsync(new DiscoveryWarning(Guid.NewGuid(), server.Id, DiscoveryWarningKind.MailNotMigrated, "alice"));

        var result = await world.Edit.HandleAsync(Command(server.Id, host: "new.example.net"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("new.example.net", result.Value.Host);
        Assert.Equal(RemoteServerState.HostKeyUnconfirmed, result.Value.State);
        Assert.Null(result.Value.HostKeyFingerprint);
        Assert.Empty(result.Value.HostKeyCandidates);
        Assert.Null(result.Value.AccountsFound);
        Assert.Empty(await world.Reader.DiscoveredAccounts.AsNoTracking().ToListAsync());
        Assert.Empty(await world.Reader.DiscoveredSites.AsNoTracking().ToListAsync());
        Assert.Empty(await world.Reader.DiscoveredDatabases.AsNoTracking().ToListAsync());
        Assert.Empty(await world.Reader.DiscoveryWarnings.AsNoTracking().ToListAsync());
        Assert.Empty(world.Agent.TestedCalls);
    }

    /// <summary>Another server's name or endpoint is a conflict; a server may keep its own.</summary>
    [Fact]
    public async Task Another_servers_name_or_endpoint_is_a_conflict_and_the_servers_own_is_not()
    {
        using var world = new MigrationHandlerWorld();
        var first = RemoteServerTestValues.Registered();
        var second = new RemoteServer(
            Guid.NewGuid(), "second", "second.example.net", 22, "maranmig", null, "ssh-ed25519 AAAAsecond", "key", RemoteServerTestValues.CreatedAt);
        await world.SeedAsync(first);
        await world.SeedAsync(second);

        var nameTaken = await world.Edit.HandleAsync(Command(second.Id, "old-host", "second.example.net"), CancellationToken.None);
        var endpointTaken = await world.Edit.HandleAsync(Command(second.Id, "second", "old.example.net"), CancellationToken.None);
        var own = await world.Edit.HandleAsync(Command(second.Id, "second", "second.example.net", 22), CancellationToken.None);

        Assert.Equal("RemoteServerNameTaken", nameTaken.Error!.Code);
        Assert.Equal("RemoteServerEndpointTaken", endpointTaken.Error!.Code);
        Assert.True(own.IsSuccess);
    }

    /// <summary>An identifier that names no server is not found and is journalled as a failure.</summary>
    [Fact]
    public async Task An_unknown_server_is_not_found()
    {
        using var world = new MigrationHandlerWorld();

        var result = await world.Edit.HandleAsync(Command(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal("RemoteServerNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.False(Assert.Single(world.Entries).Succeeded);
    }
}
