using System.Text.Json;
using Maran.Agent.Client.Services.TransferService;
using Maran.Modules.ServerMigration.Commands.AddRemoteServer;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.ServerMigration.Tests.Commands.AddRemoteServer;

/// <summary>
/// Registering a remote server: the agent makes the key and reads the host keys, nothing is stored unless both
/// succeeded, a taken name or endpoint is refused before the agent is asked, no secret reaches the answer or the
/// journal, and every outcome is journalled. Each refusal has an accepting control.
/// </summary>
public sealed class AddRemoteServerCommandHandlerTests
{
    /// <summary>A registration the tests share.</summary>
    /// <param name="name">The label.</param>
    /// <param name="host">The address, as typed.</param>
    /// <param name="port">The port.</param>
    /// <param name="allowedFrom">The key restriction, or null.</param>
    /// <returns>The command, stamped with the caller's address as the action would.</returns>
    private static AddRemoteServerCommand Command(
        string name = "old-host",
        string host = "Old.Example.NET",
        int port = 22,
        string? allowedFrom = null)
    {
        return new AddRemoteServerCommand(name, host, port, "maranmig", allowedFrom, "203.0.113.5", "tests");
    }

    /// <summary>Registering stores the server unconfirmed with the keys it offered, and journals the registration.</summary>
    [Fact]
    public async Task Registering_a_server_stores_it_unconfirmed_with_the_offered_keys_and_journals_it()
    {
        using var world = new MigrationHandlerWorld();

        var result = await world.Add.HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(RemoteServerState.HostKeyUnconfirmed, result.Value.State);
        Assert.Null(result.Value.HostKeyFingerprint);
        Assert.Equal("old.example.net", result.Value.Host);
        Assert.Equal(ScriptedTransferClient.GeneratedPublicKey, result.Value.PublicKey);
        Assert.Equal(
            [ScriptedTransferClient.FingerprintA, ScriptedTransferClient.FingerprintB],
            result.Value.HostKeyCandidates.Select(candidate =>
            {
                return candidate.Fingerprint;
            }).Order(StringComparer.Ordinal));

        var stored = await world.Reader.RemoteServers.AsNoTracking().Include(row => row.HostKeyCandidates).SingleAsync();
        Assert.Equal(result.Value.Id, stored.Id);
        Assert.Equal(ScriptedTransferClient.GeneratedPrivateKey, stored.PrivateKeyCipher);
        Assert.Equal(2, stored.HostKeyCandidates.Count);

        Assert.Equal(1, world.Agent.GenerateCalls);
        var scanned = Assert.Single(world.Agent.ScannedEndpoints);
        Assert.Equal(new AgentSourceEndpoint("old.example.net", 22, "maranmig"), scanned);

        var entry = Assert.Single(world.Entries);
        Assert.Equal(MigrationAuditActions.RemoteServerAdded, entry.Action);
        Assert.True(entry.Succeeded);
        Assert.Equal($"old.example.net ({stored.Id})", entry.Subject);
        Assert.Equal("203.0.113.5", entry.IpAddress);
    }

    /// <summary>The line to install carries the panel's public key and the restricting options, with the key restriction when asked.</summary>
    [Fact]
    public async Task The_line_to_install_carries_the_public_key_and_the_restricting_options()
    {
        using var world = new MigrationHandlerWorld();
        var options = "no-port-forwarding,no-agent-forwarding,no-X11-forwarding,no-pty";

        var open = await world.Add.HandleAsync(Command(), CancellationToken.None);
        var restricted = await world.Add.HandleAsync(
            Command("second", "second.example.net", allowedFrom: "10.0.0.1, 192.168.0.0/24"),
            CancellationToken.None);

        Assert.Equal($"{options} {ScriptedTransferClient.GeneratedPublicKey} maran-migration", open.Value.AuthorizedKeysLine);
        Assert.Null(open.Value.AllowedFrom);
        Assert.Equal("10.0.0.1/32,192.168.0.0/24", restricted.Value.AllowedFrom);
        Assert.Equal(
            $"from=\"10.0.0.1/32,192.168.0.0/24\",{options} {ScriptedTransferClient.GeneratedPublicKey} maran-migration",
            restricted.Value.AuthorizedKeysLine);
    }

    /// <summary>The private key is in the stored row only: not in the answer and not in any audit entry. The hunt finds it where it is.</summary>
    [Fact]
    public async Task The_private_key_reaches_neither_the_answer_nor_the_journal()
    {
        using var world = new MigrationHandlerWorld();
        const string sentinel = "GENERATED-PRIVATE-KEY-SENTINEL";

        var result = await world.Add.HandleAsync(Command(), CancellationToken.None);

        var answer = JsonSerializer.Serialize(result.Value);
        Assert.DoesNotContain(sentinel, answer, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE KEY", answer, StringComparison.Ordinal);
        foreach (var entry in world.Entries)
        {
            Assert.DoesNotContain(sentinel, JsonSerializer.Serialize(entry), StringComparison.Ordinal);
        }

        // The positive control: the same hunt, run over the one place the key legitimately lives, finds it.
        var stored = await world.Reader.RemoteServers.AsNoTracking().SingleAsync();
        Assert.Contains(sentinel, JsonSerializer.Serialize(new { stored.PrivateKeyCipher }), StringComparison.Ordinal);
    }

    /// <summary>A taken name is a conflict and the agent is not asked; a different name is accepted.</summary>
    [Fact]
    public async Task A_taken_name_is_a_conflict_and_the_agent_is_not_asked()
    {
        using var world = new MigrationHandlerWorld();
        await world.Add.HandleAsync(Command(), CancellationToken.None);
        var generatedBefore = world.Agent.GenerateCalls;

        var taken = await world.Add.HandleAsync(Command(host: "other.example.net"), CancellationToken.None);
        var other = await world.Add.HandleAsync(Command("other-name", "other.example.net"), CancellationToken.None);

        Assert.Equal("RemoteServerNameTaken", taken.Error!.Code);
        Assert.Equal(ErrorType.Conflict, taken.Error.Type);
        Assert.Equal(generatedBefore + 1, world.Agent.GenerateCalls);
        Assert.True(other.IsSuccess);
        Assert.Equal(2, await world.Reader.RemoteServers.CountAsync());
        Assert.Contains(world.Entries, entry =>
        {
            return entry.Action == MigrationAuditActions.RemoteServerAdded && !entry.Succeeded;
        });
    }

    /// <summary>A taken endpoint (host, port and login) is a conflict however the host was typed; another port is accepted.</summary>
    [Fact]
    public async Task A_taken_endpoint_is_a_conflict_however_the_host_was_typed()
    {
        using var world = new MigrationHandlerWorld();
        await world.Add.HandleAsync(Command(), CancellationToken.None);

        var taken = await world.Add.HandleAsync(Command("second", "OLD.example.net"), CancellationToken.None);
        var otherPort = await world.Add.HandleAsync(Command("third", "old.example.net", 2222), CancellationToken.None);

        Assert.Equal("RemoteServerEndpointTaken", taken.Error!.Code);
        Assert.Equal(ErrorType.Conflict, taken.Error.Type);
        Assert.True(otherPort.IsSuccess);
    }

    /// <summary>A server whose host keys cannot be read is not stored, with the reason the agent stated; the accepting control stores one.</summary>
    /// <param name="reason">Why the source refused.</param>
    /// <param name="code">The code the administrator is answered with.</param>
    /// <param name="type">The kind of that answer.</param>
    [Theory]
    [InlineData(SourceFailureReason.Unreachable, "RemoteServerUnreachable", ErrorType.Unavailable)]
    [InlineData(SourceFailureReason.AddressRefused, "RemoteServerAddressRefused", ErrorType.Validation)]
    [InlineData(SourceFailureReason.Unspecified, "MigrationDiscoveryFailed", ErrorType.Failure)]
    public async Task A_server_whose_host_keys_cannot_be_read_is_not_stored(
        SourceFailureReason reason,
        string code,
        ErrorType type)
    {
        using var world = new MigrationHandlerWorld();
        world.Agent.Scanned = ScriptedTransferClient.Failure<IReadOnlyList<AgentScannedHostKey>>(reason);

        var refused = await world.Add.HandleAsync(Command(), CancellationToken.None);

        Assert.Equal(code, refused.Error!.Code);
        Assert.Equal(type, refused.Error.Type);
        Assert.Empty(await world.Reader.RemoteServers.AsNoTracking().ToListAsync());
        var entry = Assert.Single(world.Entries);
        Assert.False(entry.Succeeded);
        Assert.Equal("old.example.net:22", entry.Subject);

        world.Agent.Scanned = new ScriptedTransferClient().Scanned;
        var accepted = await world.Add.HandleAsync(Command(), CancellationToken.None);
        Assert.True(accepted.IsSuccess);
        Assert.Single(await world.Reader.RemoteServers.AsNoTracking().ToListAsync());
    }

    /// <summary>An agent that cannot be reached is answered as the agent's outage and stores nothing.</summary>
    [Fact]
    public async Task An_agent_that_cannot_be_reached_is_answered_as_an_outage()
    {
        using var world = new MigrationHandlerWorld();
        world.Agent.Generated = ScriptedTransferClient.AgentDown<AgentGeneratedSourceKey>();

        var refused = await world.Add.HandleAsync(Command(), CancellationToken.None);

        Assert.Equal("MigrationAgentUnavailable", refused.Error!.Code);
        Assert.Equal(ErrorType.Unavailable, refused.Error.Type);
        Assert.Empty(world.Agent.ScannedEndpoints);
        Assert.Empty(await world.Reader.RemoteServers.AsNoTracking().ToListAsync());
    }
}
