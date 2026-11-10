using Maran.Agent.Client.Services.TransferService;
using Maran.Modules.ServerMigration.Commands.RescanRemoteServerHostKeys;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.ServerMigration.Tests.Commands.RescanRemoteServerHostKeys;

/// <summary>
/// Rescanning a server's host keys: the candidates are replaced, the pinned key is never touched, and a scan that
/// fails leaves what the administrator was looking at.
/// </summary>
public sealed class RescanRemoteServerHostKeysCommandHandlerTests
{
    /// <summary>The fingerprint of a key the server has rotated to.</summary>
    private const string RotatedFingerprint = "SHA256:CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";

    /// <summary>A rescan of a pinned server replaces the candidates and leaves the pinned key and state exactly as they were.</summary>
    [Fact]
    public async Task A_rescan_replaces_the_candidates_and_never_pins()
    {
        using var world = new MigrationHandlerWorld();
        var server = RemoteServerTestValues.Ready();
        await world.SeedAsync(server);
        world.Agent.Scanned = AgentSourceResult.Ok<IReadOnlyList<AgentScannedHostKey>>(
            [new AgentScannedHostKey("ssh-ed25519", "ssh-ed25519 AAAArotated", RotatedFingerprint)]);

        var result = await world.Rescan.HandleAsync(
            new RescanRemoteServerHostKeysCommand(server.Id, "203.0.113.5", "tests"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var candidate = Assert.Single(result.Value.HostKeyCandidates);
        Assert.Equal(RotatedFingerprint, candidate.Fingerprint);
        Assert.Equal(RemoteServerTestValues.FingerprintA, result.Value.HostKeyFingerprint);
        Assert.Equal(RemoteServerState.Ready, result.Value.State);
        var stored = await world.Reader.RemoteServers.AsNoTracking().Include(row => row.HostKeyCandidates).SingleAsync();
        Assert.Equal(RemoteServerTestValues.FingerprintA, stored.HostKeyFingerprint);
        Assert.Equal([RotatedFingerprint], stored.HostKeyCandidates.Select(row =>
        {
            return row.Fingerprint;
        }));
        Assert.Equal(server.Host, Assert.Single(world.Agent.ScannedEndpoints).Host);
    }

    /// <summary>A scan that fails changes nothing and says why; the control scans the same server successfully.</summary>
    [Fact]
    public async Task A_scan_that_fails_leaves_the_candidates_as_they_were()
    {
        using var world = new MigrationHandlerWorld();
        var server = RemoteServerTestValues.Scanned();
        await world.SeedAsync(server);
        world.Agent.Scanned = ScriptedTransferClient.Failure<IReadOnlyList<AgentScannedHostKey>>(SourceFailureReason.Unreachable);

        var refused = await world.Rescan.HandleAsync(
            new RescanRemoteServerHostKeysCommand(server.Id, "203.0.113.5", "tests"), CancellationToken.None);

        Assert.Equal("RemoteServerUnreachable", refused.Error!.Code);
        Assert.Equal(ErrorType.Unavailable, refused.Error.Type);
        var stored = await world.Reader.RemoteServers.AsNoTracking().Include(row => row.HostKeyCandidates).SingleAsync();
        Assert.Equal(2, stored.HostKeyCandidates.Count);
        Assert.False(Assert.Single(world.Entries).Succeeded);

        world.Agent.Scanned = new ScriptedTransferClient().Scanned;
        var accepted = await world.Rescan.HandleAsync(
            new RescanRemoteServerHostKeysCommand(server.Id, "203.0.113.5", "tests"), CancellationToken.None);
        Assert.True(accepted.IsSuccess);
    }

    /// <summary>An unknown server is not found and the agent is not asked.</summary>
    [Fact]
    public async Task An_unknown_server_is_not_found()
    {
        using var world = new MigrationHandlerWorld();

        var result = await world.Rescan.HandleAsync(
            new RescanRemoteServerHostKeysCommand(Guid.NewGuid(), "203.0.113.5", "tests"), CancellationToken.None);

        Assert.Equal("RemoteServerNotFound", result.Error!.Code);
        Assert.Empty(world.Agent.ScannedEndpoints);
    }
}
