using System.Text.Json;
using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Queries.GetRemoteServer;
using Maran.Modules.ServerMigration.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.ServerMigration.Tests.Queries.GetRemoteServer;

/// <summary>Reading one remote server: the detail carries the public key and the line to install, never the private key.</summary>
public sealed class GetRemoteServerQueryHandlerTests
{
    /// <summary>The detail carries the host keys on offer, the last test's findings, the warnings worded for the caller and the line to install.</summary>
    [Fact]
    public async Task The_detail_carries_everything_the_screen_needs()
    {
        using var world = new MigrationHandlerWorld();
        var server = RemoteServerTestValues.Ready();
        await world.SeedAsync(server);
        await world.SeedAsync(new DiscoveryWarning(Guid.NewGuid(), server.Id, DiscoveryWarningKind.DiskUnreadable, "alice"));

        var result = await world.Get.HandleAsync(new GetRemoteServerQuery(server.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var detail = result.Value;
        Assert.Equal(server.PublicKey, detail.PublicKey);
        Assert.Equal(
            $"no-port-forwarding,no-agent-forwarding,no-X11-forwarding,no-pty {server.PublicKey} maran-migration",
            detail.AuthorizedKeysLine);
        Assert.Equal(2, detail.HostKeyCandidates.Count);
        Assert.Equal("ssh-ed25519", detail.HostKeyAlgorithm);
        Assert.Equal(RemoteServerState.Ready, detail.State);
        Assert.Equal("RemoteServerStateReady", detail.StateName);
        Assert.Equal("Debian GNU/Linux 12", detail.SystemRelease);
        Assert.Null(detail.LastFailure);
        Assert.Null(detail.LastFailureName);
        var warning = Assert.Single(detail.Warnings);
        Assert.Equal("DiscoveryWarningDiskUnreadable", warning.Message);
    }

    /// <summary>A failed server names its failure in the caller's language.</summary>
    [Fact]
    public async Task A_failed_server_names_its_failure()
    {
        using var world = new MigrationHandlerWorld();
        var server = RemoteServerTestValues.Pinned();
        server.RecordTestFailed(RemoteFailureKind.AuthRefused, RemoteServerTestValues.LaterAt);
        await world.SeedAsync(server);

        var result = await world.Get.HandleAsync(new GetRemoteServerQuery(server.Id), CancellationToken.None);

        Assert.Equal(RemoteFailureKind.AuthRefused, result.Value.LastFailure);
        Assert.Equal("RemoteFailureKindAuthRefused", result.Value.LastFailureName);
        Assert.Equal(RemoteServerState.AuthRefused, result.Value.State);
    }

    /// <summary>The private key is not in the detail; the hunt finds it in the row that holds it, so it was able to look.</summary>
    [Fact]
    public async Task The_private_key_is_not_in_the_detail()
    {
        using var world = new MigrationHandlerWorld();
        var server = RemoteServerTestValues.Ready();
        await world.SeedAsync(server);

        var result = await world.Get.HandleAsync(new GetRemoteServerQuery(server.Id), CancellationToken.None);

        Assert.DoesNotContain(
            RemoteServerTestValues.PrivateKeySentinel, JsonSerializer.Serialize(result.Value), StringComparison.Ordinal);
        Assert.Contains(
            RemoteServerTestValues.PrivateKeySentinel, JsonSerializer.Serialize(server), StringComparison.Ordinal);
    }

    /// <summary>An unknown server is not found.</summary>
    [Fact]
    public async Task An_unknown_server_is_not_found()
    {
        using var world = new MigrationHandlerWorld();

        var result = await world.Get.HandleAsync(new GetRemoteServerQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal("RemoteServerNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }
}
