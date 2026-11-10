using Maran.Modules.Updates.Common;
using Maran.Modules.Updates.Queries.GetPanelUpdate;
using Maran.Modules.Updates.Tests.TestSupport;

namespace Maran.Modules.Updates.Tests.Queries.GetPanelUpdate;

/// <summary>What the "Panel updates" screen is told about the release state.</summary>
public sealed class GetPanelUpdateQueryHandlerTests
{
    /// <summary>The serializer settings the panel's API writes with, built once.</summary>
    private static readonly System.Text.Json.JsonSerializerOptions WebOptions =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    /// <summary>Runs the query against a world and a running version.</summary>
    /// <param name="world">The isolated server.</param>
    /// <param name="running">The running version.</param>
    /// <returns>The answer.</returns>
    private static async Task<PanelUpdateDto> AskAsync(UpdatesWorld world, string running)
    {
        var handler = new GetPanelUpdateQueryHandler(world.Reader, new FakePanelVersion(running));
        var result = await handler.HandleAsync(new GetPanelUpdateQuery(), CancellationToken.None);
        return result.Value;
    }

    /// <summary>A newer release from the last check is offered with its date, notes and the time of the check.</summary>
    [Fact]
    public async Task A_newer_release_from_the_last_check_is_offered()
    {
        using var world = new UpdatesWorld();
        world.WriteReleaseState("CHANNEL=beta\nVERSION=1.0.0\nPREVIOUS=0.9.0\n");
        world.InstallRelease("0.9.0");
        world.WriteCheckState(
            $"CHECKED_AT={world.Instant(TimeSpan.FromHours(2))}\nAVAILABLE=1.1.0\nRELEASED=2026-11-05\nNOTES_URL=https://example.net/notes/1.1.0\n");

        var dto = await AskAsync(world, "1.0.0");

        Assert.Equal("1.0.0", dto.CurrentVersion);
        Assert.Equal("beta", dto.Channel);
        Assert.Equal("0.9.0", dto.PreviousVersion);
        Assert.NotNull(dto.Available);
        Assert.Equal("1.1.0", dto.Available.Version);
        Assert.Equal("2026-11-05", dto.Available.ReleasedAt);
        Assert.Equal("https://example.net/notes/1.1.0", dto.Available.NotesUrl);
        Assert.Equal(world.Clock.UtcNow - TimeSpan.FromHours(2), dto.LastCheckedAt);
        Assert.False(dto.UpdateInProgress);
        Assert.True(dto.CanStart);
    }

    /// <summary>A check that names the running release, or an older one, offers nothing, while the time of the check is still shown.</summary>
    [Theory]
    [InlineData("1.1.0")]
    [InlineData("1.0.0")]
    [InlineData("0.9.0")]
    public async Task A_check_that_is_not_newer_than_the_running_release_offers_nothing(string checkedVersion)
    {
        using var world = new UpdatesWorld();
        world.WriteCheckState($"CHECKED_AT={world.Instant(TimeSpan.FromHours(2))}\nAVAILABLE={checkedVersion}\n");

        var dto = await AskAsync(world, "1.1.0");

        Assert.Null(dto.Available);
        Assert.NotNull(dto.LastCheckedAt);
    }

    /// <summary>A pre-release is older than its release, so the running pre-release is offered its final version.</summary>
    [Fact]
    public async Task A_final_release_is_newer_than_the_running_pre_release_of_it()
    {
        using var world = new UpdatesWorld();
        world.WriteCheckState($"CHECKED_AT={world.Instant(TimeSpan.FromHours(2))}\nAVAILABLE=1.0.0\n");

        var dto = await AskAsync(world, "1.0.0-beta.4");

        Assert.NotNull(dto.Available);
        Assert.Equal("1.0.0", dto.Available.Version);
    }

    /// <summary>Never checked, nothing is offered and the time is empty.</summary>
    [Fact]
    public async Task When_nothing_was_ever_checked_nothing_is_offered()
    {
        using var world = new UpdatesWorld();

        var dto = await AskAsync(world, "1.0.0");

        Assert.Null(dto.Available);
        Assert.Null(dto.LastCheckedAt);
        Assert.Null(dto.PreviousVersion);
        Assert.Equal("stable", dto.Channel);
    }

    /// <summary>A queued or running update shows as in progress.</summary>
    [Fact]
    public async Task A_queued_update_shows_as_in_progress()
    {
        using var world = new UpdatesWorld();
        world.PlantMarker(TimeSpan.FromSeconds(10));

        Assert.True((await AskAsync(world, "1.0.0")).UpdateInProgress);
    }

    /// <summary>A young check marker means a check is queued, and says nothing about an update.</summary>
    [Fact]
    public async Task A_young_check_marker_means_a_check_is_in_progress_and_no_update_is()
    {
        using var world = new UpdatesWorld();
        world.PlantCheckMarker(TimeSpan.FromSeconds(10));

        var dto = await AskAsync(world, "1.0.0");

        Assert.True(dto.CheckInProgress);
        Assert.False(dto.UpdateInProgress);
    }

    /// <summary>With no check marker no check is in progress, and a marker nobody took within the grace period is not a check in flight.</summary>
    [Fact]
    public async Task No_marker_and_a_stale_check_marker_mean_no_check_is_in_progress()
    {
        using var world = new UpdatesWorld();
        Assert.False((await AskAsync(world, "1.0.0")).CheckInProgress);

        world.PlantCheckMarker(TimeSpan.FromHours(1));

        Assert.False((await AskAsync(world, "1.0.0")).CheckInProgress);
    }

    /// <summary>The wire names are exactly the ones the SPA's types declare.</summary>
    [Fact]
    public void The_wire_names_are_the_ones_the_screen_reads()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(
            new PanelUpdateDto("1.0.0", "stable", new PanelUpdateAvailableDto("1.1.0", null, null), null, null, false, false, true),
            WebOptions);

        Assert.Equal(
            "{\"currentVersion\":\"1.0.0\",\"channel\":\"stable\",\"available\":{\"version\":\"1.1.0\",\"releasedAt\":null,\"notesUrl\":null},"
            + "\"lastCheckedAt\":null,\"previousVersion\":null,\"updateInProgress\":false,\"checkInProgress\":false,\"canStart\":true}",
            json);
    }
}
