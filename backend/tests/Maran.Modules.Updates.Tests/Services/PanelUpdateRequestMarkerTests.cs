using Maran.Modules.Updates.Domain.Enums;
using Maran.Modules.Updates.Services;
using Maran.Modules.Updates.Tests.TestSupport;

namespace Maran.Modules.Updates.Tests.Services;

/// <summary>The marker is the one thing the panel hands to root: it must be a fixed message, created exclusively.</summary>
public sealed class PanelUpdateRequestMarkerTests
{
    /// <summary>The marker holds exactly the fixed token and a newline, and nothing a caller chose.</summary>
    [Fact]
    public void The_marker_holds_exactly_the_fixed_token()
    {
        using var world = new UpdatesWorld();

        Assert.Equal(PanelUpdateMarkerOutcome.Created, world.Marker.TryCreate());

        Assert.Equal("update\n", File.ReadAllText(world.Options.MarkerPath));
        Assert.Equal("update\n", PanelUpdateRequestMarker.Content);
    }

    /// <summary>A second request while the marker is there is told so and leaves the marker untouched.</summary>
    [Fact]
    public void A_second_request_finds_the_marker_already_pending_and_leaves_it_alone()
    {
        using var world = new UpdatesWorld();
        Assert.Equal(PanelUpdateMarkerOutcome.Created, world.Marker.TryCreate());
        File.SetLastWriteTimeUtc(world.Options.MarkerPath, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(PanelUpdateMarkerOutcome.AlreadyPending, world.Marker.TryCreate());

        Assert.Equal(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), File.GetLastWriteTimeUtc(world.Options.MarkerPath));
    }

    /// <summary>A link planted at the marker's name is never followed or written through.</summary>
    [Fact]
    public void A_planted_link_is_never_written_through()
    {
        using var world = new UpdatesWorld();
        var target = Path.Combine(world.Options.RequestDirectory, "victim");
        File.WriteAllText(target, "untouched");
        File.CreateSymbolicLink(world.Options.MarkerPath, target);

        Assert.Equal(PanelUpdateMarkerOutcome.AlreadyPending, world.Marker.TryCreate());

        Assert.Equal("untouched", File.ReadAllText(target));
    }

    /// <summary>Without the request directory the marker cannot be created and the outcome carries no path.</summary>
    [Fact]
    public void Without_the_request_directory_the_marker_cannot_be_created()
    {
        using var world = new UpdatesWorld(createRequestDirectory: false);

        Assert.Equal(PanelUpdateMarkerOutcome.Failed, world.Marker.TryCreate());
    }

    /// <summary>A marker can be withdrawn, and withdrawing a marker that is not there succeeds.</summary>
    [Fact]
    public void A_marker_can_be_withdrawn_and_withdrawing_nothing_succeeds()
    {
        using var world = new UpdatesWorld();
        Assert.Equal(PanelUpdateMarkerOutcome.Created, world.Marker.TryCreate());

        Assert.True(world.Marker.TryRemove());
        Assert.False(world.MarkerExists());
        Assert.True(world.Marker.TryRemove());
    }
}
