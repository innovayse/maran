using Maran.Modules.ServerMigration.Commands.RemoveRemoteServer;
using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.ServerMigration.Tests.Commands.RemoveRemoteServer;

/// <summary>
/// Removing a remote server: everything stored about it goes, the answer names the public key still installed on the
/// server and never the private one, and another server's rows are not touched.
/// </summary>
public sealed class RemoveRemoteServerCommandHandlerTests
{
    /// <summary>A removal of the server the tests seed.</summary>
    /// <param name="serverId">The server.</param>
    /// <returns>The command, stamped as the action would.</returns>
    private static RemoveRemoteServerCommand Command(Guid serverId)
    {
        return new RemoveRemoteServerCommand(serverId, "203.0.113.5", "tests");
    }

    /// <summary>Removal deletes the server, its host keys and its discovery, and keeps a neighbour's.</summary>
    [Fact]
    public async Task Removal_deletes_the_server_and_everything_stored_about_it()
    {
        using var world = new MigrationHandlerWorld();
        var server = RemoteServerTestValues.Ready();
        var neighbour = new RemoteServer(
            Guid.NewGuid(), "neighbour", "neighbour.example.net", 22, "maranmig", null, "ssh-ed25519 AAAAn", "key", RemoteServerTestValues.CreatedAt);
        await world.SeedAsync(server);
        await world.SeedAsync(neighbour);
        var account = new DiscoveredAccount(Guid.NewGuid(), server.Id, "alice", "/home/alice", 10, 0, 0);
        account.AddSite(Guid.NewGuid(), "alice.example", ["www.alice.example"], DiscoveredSiteBackend.Php, "8.2", "/home/alice/public_html");
        account.AddDatabase(Guid.NewGuid(), "alice_db", 5);
        await world.SeedAsync(account);
        await world.SeedAsync(new DiscoveredAccount(Guid.NewGuid(), neighbour.Id, "bob", "/home/bob", 10, 0, 0));
        await world.SeedAsync(new DiscoveryWarning(Guid.NewGuid(), server.Id, DiscoveryWarningKind.MailNotMigrated, "alice"));

        var result = await world.Remove.HandleAsync(Command(server.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var remaining = await world.Reader.RemoteServers.AsNoTracking().SingleAsync();
        Assert.Equal(neighbour.Id, remaining.Id);
        Assert.Empty(await world.Reader.RemoteServerHostKeyCandidates.AsNoTracking().ToListAsync());
        Assert.Equal("bob", (await world.Reader.DiscoveredAccounts.AsNoTracking().SingleAsync()).Name);
        Assert.Empty(await world.Reader.DiscoveredSites.AsNoTracking().ToListAsync());
        Assert.Empty(await world.Reader.DiscoveredDatabases.AsNoTracking().ToListAsync());
        Assert.Empty(await world.Reader.DiscoveryWarnings.AsNoTracking().ToListAsync());
        var entry = Assert.Single(world.Entries);
        Assert.Equal(MigrationAuditActions.RemoteServerRemoved, entry.Action);
        Assert.True(entry.Succeeded);
    }

    /// <summary>The answer names the public key still installed and the pinned fingerprint, and never the private key.</summary>
    [Fact]
    public async Task The_answer_names_the_public_key_to_take_out_and_never_the_private_one()
    {
        using var world = new MigrationHandlerWorld();
        var server = RemoteServerTestValues.Ready();
        await world.SeedAsync(server);

        var result = await world.Remove.HandleAsync(Command(server.Id), CancellationToken.None);

        Assert.Equal(server.PublicKey, result.Value.PublicKey);
        Assert.Equal(RemoteServerTestValues.FingerprintA, result.Value.HostKeyFingerprint);
        Assert.DoesNotContain(
            RemoteServerTestValues.PrivateKeySentinel,
            System.Text.Json.JsonSerializer.Serialize(result.Value),
            StringComparison.Ordinal);
    }

    /// <summary>A server that never had a host key pinned answers with no fingerprint; an unknown server is not found.</summary>
    [Fact]
    public async Task An_unconfirmed_server_has_no_fingerprint_and_an_unknown_one_is_not_found()
    {
        using var world = new MigrationHandlerWorld();
        var server = RemoteServerTestValues.Scanned();
        await world.SeedAsync(server);

        var removed = await world.Remove.HandleAsync(Command(server.Id), CancellationToken.None);
        var missing = await world.Remove.HandleAsync(Command(Guid.NewGuid()), CancellationToken.None);

        Assert.Null(removed.Value.HostKeyFingerprint);
        Assert.Equal("RemoteServerNotFound", missing.Error!.Code);
        Assert.Equal(ErrorType.NotFound, missing.Error.Type);
        Assert.False(world.Entries[1].Succeeded);
    }
}
