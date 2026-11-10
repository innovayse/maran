using Maran.Agent.Client.Services.TransferService;
using Maran.Modules.ServerMigration.Commands.TestRemoteServer;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.ServerMigration.Tests.Commands.TestRemoteServer;

/// <summary>
/// Testing a remote server: it needs a pinned host key, it sends the agent the key and the pinned host key and
/// nothing else, a refusal that describes the server is recorded on its row, and one that describes the agent is not.
/// </summary>
public sealed class TestRemoteServerCommandHandlerTests
{
    /// <summary>A test of the server the tests seed.</summary>
    /// <param name="serverId">The server.</param>
    /// <returns>The command, stamped as the action would.</returns>
    private static TestRemoteServerCommand Command(Guid serverId)
    {
        return new TestRemoteServerCommand(serverId, "203.0.113.5", "tests");
    }

    /// <summary>A server with no pinned host key is refused before the agent is asked; the pinned control is tested.</summary>
    [Fact]
    public async Task A_server_without_a_pinned_host_key_is_refused_before_the_agent_is_asked()
    {
        using var world = new MigrationHandlerWorld();
        var unconfirmed = RemoteServerTestValues.Scanned();
        await world.SeedAsync(unconfirmed);

        var refused = await world.Test.HandleAsync(Command(unconfirmed.Id), CancellationToken.None);

        Assert.Equal("RemoteServerHostKeyUnconfirmed", refused.Error!.Code);
        Assert.Equal(ErrorType.Conflict, refused.Error.Type);
        Assert.Empty(world.Agent.TestedCalls);

        var pinned = RemoteServerTestValues.Pinned();
        await world.SeedAsync(pinned);
        var accepted = await world.Test.HandleAsync(Command(pinned.Id), CancellationToken.None);
        Assert.True(accepted.IsSuccess);
        Assert.Single(world.Agent.TestedCalls);
    }

    /// <summary>A passing test records what the probes found, makes the server ready, and sends the pinned key and the private key.</summary>
    [Fact]
    public async Task A_passing_test_records_what_the_probes_found()
    {
        using var world = new MigrationHandlerWorld();
        var server = RemoteServerTestValues.Pinned();
        await world.SeedAsync(server);

        var result = await world.Test.HandleAsync(Command(server.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(RemoteServerState.Ready, result.Value.State);
        Assert.Equal(RemoteSourceKind.GenericLinux, result.Value.Kind);
        Assert.Equal("Debian GNU/Linux 12", result.Value.SystemRelease);
        Assert.Equal("MariaDb", result.Value.DatabaseServer);
        Assert.Equal("Nginx", result.Value.WebServer);
        Assert.True(result.Value.MailFound);
        Assert.False(result.Value.IsRoot);
        Assert.Equal(1_000, result.Value.HomeFreeBytes);
        Assert.Equal(world.Clock.UtcNow, result.Value.LastTestedAt);
        Assert.Null(result.Value.LastFailure);

        var (endpoint, credential) = Assert.Single(world.Agent.TestedCalls);
        Assert.Equal(new AgentSourceEndpoint(server.Host, server.Port, server.SshUser), endpoint);
        Assert.Equal(server.HostKey, credential.HostKey);
        Assert.Equal(RemoteServerTestValues.PrivateKeySentinel, credential.PrivateKey.Reveal());
        Assert.DoesNotContain(RemoteServerTestValues.PrivateKeySentinel, credential.ToString(), StringComparison.Ordinal);

        var entry = Assert.Single(world.Entries);
        Assert.Equal(MigrationAuditActions.RemoteServerTested, entry.Action);
        Assert.True(entry.Succeeded);
        Assert.DoesNotContain(RemoteServerTestValues.PrivateKeySentinel, entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>A refusal that describes the server is written to its row and answered with its own code.</summary>
    /// <param name="reason">Why the source refused.</param>
    /// <param name="code">The code the administrator is answered with.</param>
    /// <param name="type">The kind of that answer.</param>
    /// <param name="state">The state the server is left in.</param>
    /// <param name="failure">The failure recorded on the row.</param>
    [Theory]
    [InlineData(SourceFailureReason.HostKeyChanged, "RemoteServerHostKeyChanged", ErrorType.Conflict, RemoteServerState.HostKeyChanged, RemoteFailureKind.HostKeyChanged)]
    [InlineData(SourceFailureReason.AuthRefused, "RemoteServerAuthRefused", ErrorType.Conflict, RemoteServerState.AuthRefused, RemoteFailureKind.AuthRefused)]
    [InlineData(SourceFailureReason.Unreachable, "RemoteServerUnreachable", ErrorType.Unavailable, RemoteServerState.Unreachable, RemoteFailureKind.Unreachable)]
    [InlineData(SourceFailureReason.AddressRefused, "RemoteServerAddressRefused", ErrorType.Validation, RemoteServerState.Unreachable, RemoteFailureKind.AddressRefused)]
    public async Task A_refusal_that_describes_the_server_is_recorded_on_its_row(
        SourceFailureReason reason,
        string code,
        ErrorType type,
        RemoteServerState state,
        RemoteFailureKind failure)
    {
        using var world = new MigrationHandlerWorld();
        var server = RemoteServerTestValues.Ready();
        await world.SeedAsync(server);
        world.Agent.Tested = ScriptedTransferClient.Failure<AgentTestedSource>(reason);

        var refused = await world.Test.HandleAsync(Command(server.Id), CancellationToken.None);

        Assert.Equal(code, refused.Error!.Code);
        Assert.Equal(type, refused.Error.Type);
        var stored = await world.Reader.RemoteServers.AsNoTracking().SingleAsync();
        Assert.Equal(state, stored.State);
        Assert.Equal(failure, stored.LastFailure);
        Assert.Equal(world.Clock.UtcNow, stored.LastTestedAt);
        Assert.Null(stored.IsRoot);
        Assert.Equal(RemoteServerTestValues.FingerprintA, stored.HostKeyFingerprint);
        var entry = Assert.Single(world.Entries);
        Assert.False(entry.Succeeded);
        Assert.Equal(MigrationAuditActions.RemoteServerTested, entry.Action);
    }

    /// <summary>An agent that cannot be reached says nothing about the server: its row keeps what it had.</summary>
    [Fact]
    public async Task An_agent_outage_leaves_the_server_row_as_it_was()
    {
        using var world = new MigrationHandlerWorld();
        var server = RemoteServerTestValues.Ready();
        await world.SeedAsync(server);
        world.Agent.Tested = ScriptedTransferClient.AgentDown<AgentTestedSource>();

        var refused = await world.Test.HandleAsync(Command(server.Id), CancellationToken.None);

        Assert.Equal("MigrationAgentUnavailable", refused.Error!.Code);
        Assert.Equal(ErrorType.Unavailable, refused.Error.Type);
        var stored = await world.Reader.RemoteServers.AsNoTracking().SingleAsync();
        Assert.Equal(RemoteServerState.Ready, stored.State);
        Assert.Null(stored.LastFailure);
        Assert.Equal(RemoteServerTestValues.LaterAt, stored.LastTestedAt);
    }

    /// <summary>An unknown server is not found and the agent is not asked.</summary>
    [Fact]
    public async Task An_unknown_server_is_not_found()
    {
        using var world = new MigrationHandlerWorld();

        var result = await world.Test.HandleAsync(Command(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal("RemoteServerNotFound", result.Error!.Code);
        Assert.Empty(world.Agent.TestedCalls);
    }
}
