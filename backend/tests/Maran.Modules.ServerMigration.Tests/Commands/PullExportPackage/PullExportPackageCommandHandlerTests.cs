using Maran.Modules.ServerMigration.Commands.PullExportPackage;
using Maran.Modules.ServerMigration.Models;
using Maran.Modules.ServerMigration.Tests.TestSupport;

namespace Maran.Modules.ServerMigration.Tests.Commands.PullExportPackage;

/// <summary>What serving the package to another panel does and does not do (issue #127).</summary>
public sealed class PullExportPackageCommandHandlerTests
{
    /// <summary>Builds the handler over a world.</summary>
    /// <param name="world">The world.</param>
    /// <returns>The handler.</returns>
    private static PullExportPackageCommandHandler HandlerOf(PullWorld world)
    {
        return new PullExportPackageCommandHandler(world.Database, world.Ledger, world.Writer, world.Clock, world.Journal);
    }

    /// <summary>The destination a test writes to, and the sizes it was told.</summary>
    /// <param name="body">The body.</param>
    /// <returns>The destination.</returns>
    private static PackageDestination Into(MemoryStream body)
    {
        return new PackageDestination(body, _ => { });
    }

    /// <summary>When the ledger refuses the single read, nothing is written, the agent is never asked, and the refusal is the generic not-found.</summary>
    [Fact]
    public async Task A_refused_read_writes_nothing_and_never_reaches_the_agent()
    {
        var world = new PullWorld(nameof(A_refused_read_writes_nothing_and_never_reaches_the_agent));
        world.Ledger.ArtifactAllowed = false;
        var body = new MemoryStream();

        var result = await HandlerOf(world).HandleAsync(new PullExportPackageCommand(world.Grant.Id, Into(body), "198.51.100.7", "agent"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("MigrationExportNotFound", result.Error!.Code);
        Assert.Equal(0, body.Length);
        Assert.Empty(world.Agent.ExportCalls);
        var entry = Assert.Single(world.Audit.Entries);
        Assert.False(entry.Succeeded);
        Assert.StartsWith("export-grant:", entry.ActorUsername, StringComparison.Ordinal);
    }

    /// <summary>An expired export does not burn the grant's read: the ledger is not asked at all.</summary>
    [Fact]
    public async Task An_expired_export_does_not_spend_the_read()
    {
        var world = new PullWorld(nameof(An_expired_export_does_not_spend_the_read));
        world.Clock.UtcNow = world.Export.ExpiresAt.AddMinutes(1);

        var result = await HandlerOf(world).HandleAsync(
            new PullExportPackageCommand(world.Grant.Id, Into(new MemoryStream()), "198.51.100.7", "agent"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, world.Ledger.ArtifactAsked);
    }

    /// <summary>A grant id nobody holds is the same refusal and asks the ledger for nothing.</summary>
    [Fact]
    public async Task An_unknown_grant_is_the_same_refusal()
    {
        var world = new PullWorld(nameof(An_unknown_grant_is_the_same_refusal));

        var result = await HandlerOf(world).HandleAsync(
            new PullExportPackageCommand(Guid.NewGuid(), Into(new MemoryStream()), "198.51.100.7", "agent"), CancellationToken.None);

        Assert.Equal("MigrationExportNotFound", result.Error!.Code);
        Assert.Equal(0, world.Ledger.ArtifactAsked);
    }

    /// <summary>The accepting control: when the ledger allows the read the agent is asked for the export of this account and backup.</summary>
    [Fact]
    public async Task An_allowed_read_asks_the_agent_for_this_exports_backup()
    {
        var world = new PullWorld(nameof(An_allowed_read_asks_the_agent_for_this_exports_backup));

        await HandlerOf(world).HandleAsync(
            new PullExportPackageCommand(world.Grant.Id, Into(new MemoryStream()), "198.51.100.7", "agent"), CancellationToken.None);

        Assert.Equal(1, world.Ledger.ArtifactAsked);
        Assert.Equal([("alice", world.Export.BackupId.ToString())], world.Agent.ExportCalls);
    }
}
