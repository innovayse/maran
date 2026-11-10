using Maran.Modules.ServerMigration.Commands.PullExportHead;
using Maran.Modules.ServerMigration.Models;
using Maran.Modules.ServerMigration.Tests.TestSupport;

namespace Maran.Modules.ServerMigration.Tests.Commands.PullExportHead;

/// <summary>What serving the preview of a package to another panel does and does not do (issue #127).</summary>
public sealed class PullExportHeadCommandHandlerTests
{
    /// <summary>When the ledger refuses the read (the reads are used up, or the artifact was taken), nothing is written and the agent is not asked.</summary>
    [Fact]
    public async Task A_refused_manifest_read_writes_nothing_and_never_reaches_the_agent()
    {
        var world = new PullWorld(nameof(A_refused_manifest_read_writes_nothing_and_never_reaches_the_agent));
        world.Ledger.ManifestAllowed = false;
        var body = new MemoryStream();
        var handler = new PullExportHeadCommandHandler(world.Database, world.Ledger, world.Writer, world.Clock, world.Journal);

        var result = await handler.HandleAsync(
            new PullExportHeadCommand(world.Grant.Id, new PackageDestination(body, _ => { }), "198.51.100.7", "agent"), CancellationToken.None);

        Assert.Equal("MigrationExportNotFound", result.Error!.Code);
        Assert.Equal(0, body.Length);
        Assert.Empty(world.Agent.ExportCalls);
        Assert.Equal(1, world.Ledger.ManifestAsked);
    }

    /// <summary>The preview never spends the artifact read.</summary>
    [Fact]
    public async Task The_preview_never_asks_for_the_artifact_read()
    {
        var world = new PullWorld(nameof(The_preview_never_asks_for_the_artifact_read));
        var handler = new PullExportHeadCommandHandler(world.Database, world.Ledger, world.Writer, world.Clock, world.Journal);

        await handler.HandleAsync(
            new PullExportHeadCommand(world.Grant.Id, new PackageDestination(new MemoryStream(), _ => { }), "198.51.100.7", "agent"),
            CancellationToken.None);

        Assert.Equal(0, world.Ledger.ArtifactAsked);
        Assert.Equal(1, world.Ledger.ManifestAsked);
    }
}
