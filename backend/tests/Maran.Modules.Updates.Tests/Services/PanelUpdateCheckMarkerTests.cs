using Maran.Modules.Updates.Domain.Enums;
using Maran.Modules.Updates.Services;
using Maran.Modules.Updates.Tests.TestSupport;

namespace Maran.Modules.Updates.Tests.Services;

/// <summary>The check marker is the one thing a check request hands to root: a fixed message, created exclusively, apart from the update marker.</summary>
public sealed class PanelUpdateCheckMarkerTests
{
    /// <summary>The marker holds exactly the fixed token and a newline, and is not the update marker.</summary>
    [Fact]
    public void The_check_marker_holds_exactly_the_fixed_token_and_is_not_the_update_marker()
    {
        using var world = new UpdatesWorld();

        Assert.Equal(PanelUpdateMarkerOutcome.Created, world.CheckMarker.TryCreate());

        Assert.Equal("check\n", File.ReadAllText(world.Options.CheckMarkerPath));
        Assert.Equal("check\n", PanelUpdateCheckMarker.Content);
        Assert.False(world.MarkerExists(), "creating the check marker must not start an update");
        Assert.NotEqual(world.Options.MarkerPath, world.Options.CheckMarkerPath);
    }

    /// <summary>A second request while the marker is there is told so and leaves the marker untouched.</summary>
    [Fact]
    public void A_second_check_request_finds_the_marker_already_pending_and_leaves_it_alone()
    {
        using var world = new UpdatesWorld();
        Assert.Equal(PanelUpdateMarkerOutcome.Created, world.CheckMarker.TryCreate());
        File.SetLastWriteTimeUtc(world.Options.CheckMarkerPath, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(PanelUpdateMarkerOutcome.AlreadyPending, world.CheckMarker.TryCreate());

        Assert.Equal(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), File.GetLastWriteTimeUtc(world.Options.CheckMarkerPath));
    }

    /// <summary>A link planted at the marker's name is never followed or written through.</summary>
    [Fact]
    public void A_planted_link_is_never_written_through()
    {
        using var world = new UpdatesWorld();
        var target = Path.Combine(world.Options.RequestDirectory, "victim");
        File.WriteAllText(target, "untouched");
        File.CreateSymbolicLink(world.Options.CheckMarkerPath, target);

        Assert.Equal(PanelUpdateMarkerOutcome.AlreadyPending, world.CheckMarker.TryCreate());

        Assert.Equal("untouched", File.ReadAllText(target));
    }

    /// <summary>Without the request directory the marker cannot be created.</summary>
    [Fact]
    public void Without_the_request_directory_the_check_marker_cannot_be_created()
    {
        using var world = new UpdatesWorld(createRequestDirectory: false);

        Assert.Equal(PanelUpdateMarkerOutcome.Failed, world.CheckMarker.TryCreate());
    }

    /// <summary>A marker can be withdrawn, and withdrawing a marker that is not there succeeds.</summary>
    [Fact]
    public void A_check_marker_can_be_withdrawn_and_withdrawing_nothing_succeeds()
    {
        using var world = new UpdatesWorld();
        Assert.Equal(PanelUpdateMarkerOutcome.Created, world.CheckMarker.TryCreate());

        Assert.True(world.CheckMarker.TryRemove());
        Assert.False(world.CheckMarkerExists());
        Assert.True(world.CheckMarker.TryRemove());
    }
}
