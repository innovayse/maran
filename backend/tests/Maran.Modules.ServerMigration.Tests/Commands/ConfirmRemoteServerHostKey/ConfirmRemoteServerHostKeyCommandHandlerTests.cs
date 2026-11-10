using Maran.Modules.ServerMigration.Commands.ConfirmRemoteServerHostKey;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.ServerMigration.Tests.Commands.ConfirmRemoteServerHostKey;

/// <summary>
/// Confirming a host key: the fingerprint must be exactly one the server offered, anything else pins nothing, and a
/// refusal is journalled without the fingerprint that was typed.
/// </summary>
public sealed class ConfirmRemoteServerHostKeyCommandHandlerTests
{
    /// <summary>A confirmation of a fingerprint for the server the tests seed.</summary>
    /// <param name="serverId">The server.</param>
    /// <param name="fingerprint">The fingerprint typed.</param>
    /// <returns>The command, stamped as the action would.</returns>
    private static ConfirmRemoteServerHostKeyCommand Command(Guid serverId, string fingerprint)
    {
        return new ConfirmRemoteServerHostKeyCommand(fingerprint, serverId, "203.0.113.5", "tests");
    }

    /// <summary>An offered fingerprint, typed exactly, pins its key and journals the pin.</summary>
    [Fact]
    public async Task An_offered_fingerprint_typed_exactly_pins_the_key()
    {
        using var world = new MigrationHandlerWorld();
        var server = RemoteServerTestValues.Scanned();
        await world.SeedAsync(server);

        var result = await world.Confirm.HandleAsync(
            Command(server.Id, RemoteServerTestValues.FingerprintB), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(RemoteServerState.Pinned, result.Value.State);
        Assert.Equal(RemoteServerTestValues.FingerprintB, result.Value.HostKeyFingerprint);
        Assert.Equal("ssh-rsa", result.Value.HostKeyAlgorithm);
        Assert.Equal(world.Clock.UtcNow, result.Value.HostKeyConfirmedAt);
        var stored = await world.Reader.RemoteServers.AsNoTracking().SingleAsync();
        Assert.Equal("AAAArsa", stored.HostKey);
        var entry = Assert.Single(world.Entries);
        Assert.Equal(MigrationAuditActions.RemoteServerHostKeyPinned, entry.Action);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A fingerprint that differs by case, or that the server never offered, pins nothing and is journalled as a failure.</summary>
    /// <param name="typed">The fingerprint typed.</param>
    [Theory]
    [InlineData("SHA256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("SHA256:ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ")]
    public async Task A_fingerprint_that_does_not_match_exactly_pins_nothing(string typed)
    {
        using var world = new MigrationHandlerWorld();
        var server = RemoteServerTestValues.Scanned();
        await world.SeedAsync(server);

        var refused = await world.Confirm.HandleAsync(Command(server.Id, typed), CancellationToken.None);

        Assert.Equal("RemoteServerHostKeyFingerprintMismatch", refused.Error!.Code);
        Assert.Equal(ErrorType.Conflict, refused.Error.Type);
        var stored = await world.Reader.RemoteServers.AsNoTracking().SingleAsync();
        Assert.Null(stored.HostKeyFingerprint);
        Assert.Equal(RemoteServerState.HostKeyUnconfirmed, stored.State);
        var entry = Assert.Single(world.Entries);
        Assert.False(entry.Succeeded);
        Assert.DoesNotContain(typed, entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>An unknown server is not found.</summary>
    [Fact]
    public async Task An_unknown_server_is_not_found()
    {
        using var world = new MigrationHandlerWorld();

        var result = await world.Confirm.HandleAsync(
            Command(Guid.NewGuid(), RemoteServerTestValues.FingerprintA), CancellationToken.None);

        Assert.Equal("RemoteServerNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }
}
