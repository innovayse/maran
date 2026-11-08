using Maran.Modules.Updates.Domain.Policies;
using Maran.Modules.Updates.Tests.TestSupport;

namespace Maran.Modules.Updates.Tests.Services;

/// <summary>
/// What the panel makes of the files root maintains. Every file is untrusted text: a missing, garbled,
/// oversize or hostile one reads as "nothing known", and no test here expects an exception.
/// </summary>
public sealed class PanelUpdateStateReaderTests
{
    /// <summary>With no files at all the panel knows nothing, follows the stable channel, and can start an update because the request directory exists.</summary>
    [Fact]
    public void With_no_files_the_panel_knows_nothing_and_follows_the_stable_channel()
    {
        using var world = new UpdatesWorld();

        var facts = world.Reader.Read();

        Assert.Equal("stable", facts.Channel);
        Assert.Null(facts.Check);
        Assert.Null(facts.PreviousVersion);
        Assert.Null(facts.Signals.MarkerAge);
        Assert.False(facts.Signals.RunningFresh);
        Assert.True(facts.CanStart);
    }

    /// <summary>A server whose installer has not built the request directory cannot start an update.</summary>
    [Fact]
    public void Without_the_request_directory_an_update_cannot_be_started()
    {
        using var world = new UpdatesWorld(createRequestDirectory: false);

        Assert.False(world.Reader.Read().CanStart);
    }

    /// <summary>A request directory that is a link to another directory is not the directory the installer built.</summary>
    [Fact]
    public void A_request_directory_that_is_a_link_cannot_start_an_update()
    {
        using var world = new UpdatesWorld(createRequestDirectory: false);
        var elsewhere = Directory.CreateTempSubdirectory("maran-updates-elsewhere-").FullName;
        try
        {
            Directory.CreateSymbolicLink(world.Options.RequestDirectory, elsewhere);

            Assert.False(world.Reader.Read().CanStart);
        }
        finally
        {
            Directory.Delete(world.Options.RequestDirectory);
            Directory.Delete(elsewhere);
        }
    }

    /// <summary>The channel is read from the release record when it is one the installer knows.</summary>
    [Fact]
    public void The_channel_is_read_from_the_release_record()
    {
        using var world = new UpdatesWorld();
        world.WriteReleaseState("CHANNEL=beta\nVERSION=1.0.0\n");

        Assert.Equal("beta", world.Reader.Read().Channel);
    }

    /// <summary>A channel that is not one the installer knows is replaced by the default and never echoed.</summary>
    [Fact]
    public void An_unknown_channel_reads_as_the_default()
    {
        using var world = new UpdatesWorld();
        world.WriteReleaseState("CHANNEL=<script>alert(1)</script>\n");

        Assert.Equal("stable", world.Reader.Read().Channel);
    }

    /// <summary>The previous version is shown only when that release is really installed.</summary>
    [Fact]
    public void The_previous_version_is_shown_only_when_its_directory_exists()
    {
        using var world = new UpdatesWorld();
        world.WriteReleaseState("PREVIOUS=0.9.0\n");

        Assert.Null(world.Reader.Read().PreviousVersion);

        world.InstallRelease("0.9.0");

        Assert.Equal("0.9.0", world.Reader.Read().PreviousVersion);
    }

    /// <summary>A previous name that would step out of the releases directory is never turned into a path.</summary>
    [Fact]
    public void A_previous_name_that_is_a_path_is_never_followed()
    {
        using var world = new UpdatesWorld();
        world.InstallRelease("0.9.0");
        world.WriteReleaseState("PREVIOUS=../releases/0.9.0\n");

        Assert.Null(world.Reader.Read().PreviousVersion);
    }

    /// <summary>A valid check record is read with its release date and notes address.</summary>
    [Fact]
    public void A_valid_check_record_is_read_in_full()
    {
        using var world = new UpdatesWorld();
        world.WriteCheckState(
            $"CHECKED_AT={world.Instant(TimeSpan.FromHours(4))}\nCHANNEL=stable\nAVAILABLE=1.1.0\nRELEASED=2026-11-05\nNOTES_URL=https://example.net/notes/1.1.0\n");

        var check = world.Reader.Read().Check;

        Assert.NotNull(check);
        Assert.Equal("1.1.0", check.Version);
        Assert.Equal("2026-11-05", check.ReleasedAt);
        Assert.Equal("https://example.net/notes/1.1.0", check.NotesUrl);
        Assert.Equal(world.Clock.UtcNow - TimeSpan.FromHours(4), check.CheckedAt);
    }

    /// <summary>A hostile notes address or a garbled date is dropped on its own, while the rest of the record is kept.</summary>
    [Fact]
    public void A_hostile_notes_address_or_a_garbled_date_is_dropped_on_its_own()
    {
        using var world = new UpdatesWorld();
        world.WriteCheckState(
            $"CHECKED_AT={world.Instant(TimeSpan.FromHours(1))}\nAVAILABLE=1.1.0\nRELEASED=soon\nNOTES_URL=javascript:alert(1)\n");

        var check = world.Reader.Read().Check;

        Assert.NotNull(check);
        Assert.Equal("1.1.0", check.Version);
        Assert.Null(check.ReleasedAt);
        Assert.Null(check.NotesUrl);
    }

    /// <summary>A check record without a valid version or time is no check at all.</summary>
    [Theory]
    [InlineData("AVAILABLE=banana\nCHECKED_AT=2026-11-06T08:00:00Z\n")]
    [InlineData("AVAILABLE=1.1.0\nCHECKED_AT=whenever\n")]
    [InlineData("AVAILABLE=1.1.0\n")]
    [InlineData("\u0000\u0001garbage")]
    [InlineData("")]
    public void A_check_record_missing_a_valid_version_or_time_is_no_check(string text)
    {
        using var world = new UpdatesWorld();
        world.WriteCheckState(text);

        Assert.Null(world.Reader.Read().Check);
    }

    /// <summary>A record larger than the limit is refused whole, even when its first lines are valid.</summary>
    [Fact]
    public void An_oversize_check_record_is_refused_whole()
    {
        using var world = new UpdatesWorld();
        world.WriteCheckState(
            $"CHECKED_AT={world.Instant(TimeSpan.FromHours(1))}\nAVAILABLE=1.1.0\n#{new string('x', StateFileText.MaxBytes)}\n");

        Assert.Null(world.Reader.Read().Check);
    }

    /// <summary>A record that is a link to somewhere else is not read.</summary>
    [Fact]
    public void A_check_record_that_is_a_link_is_not_read()
    {
        using var world = new UpdatesWorld();
        var target = Path.Combine(Path.GetDirectoryName(world.Options.CheckStatePath)!, "elsewhere");
        File.WriteAllText(target, $"CHECKED_AT={world.Instant(TimeSpan.FromHours(1))}\nAVAILABLE=1.1.0\n");
        File.CreateSymbolicLink(world.Options.CheckStatePath, target);

        Assert.Null(world.Reader.Read().Check);
    }

    /// <summary>A young marker is an update in flight, and its age is measured from the file.</summary>
    [Fact]
    public void A_young_marker_is_an_update_in_flight()
    {
        using var world = new UpdatesWorld();
        world.PlantMarker(TimeSpan.FromMinutes(1));

        var signals = world.Reader.ReadSignals();

        Assert.NotNull(signals.MarkerAge);
        Assert.True(Math.Abs((signals.MarkerAge.GetValueOrDefault() - TimeSpan.FromMinutes(1)).TotalSeconds) < 1);
        Assert.True(PanelUpdateLedgerPolicy.IsInProgress(signals));
    }

    /// <summary>A marker whose time is in the future is treated as brand new, never as a negative age.</summary>
    [Fact]
    public void A_marker_dated_in_the_future_reads_as_brand_new()
    {
        using var world = new UpdatesWorld();
        world.PlantMarker(TimeSpan.FromMinutes(-30));

        Assert.Equal(TimeSpan.Zero, world.Reader.ReadSignals().MarkerAge);
    }

    /// <summary>A marker older than the grace period is no longer an update in flight.</summary>
    [Fact]
    public void An_old_marker_is_not_an_update_in_flight()
    {
        using var world = new UpdatesWorld();
        world.PlantMarker(TimeSpan.FromMinutes(20));

        Assert.False(PanelUpdateLedgerPolicy.IsInProgress(world.Reader.ReadSignals()));
    }

    /// <summary>Root's running record is believed while it is young and ignored once it is old or malformed.</summary>
    [Fact]
    public void Roots_running_record_is_believed_only_while_it_is_fresh_and_well_formed()
    {
        using var world = new UpdatesWorld();

        world.WriteRunningState($"STARTED_AT={world.Instant(TimeSpan.FromMinutes(10))}\n");
        Assert.True(world.Reader.ReadSignals().RunningFresh);

        world.WriteRunningState($"STARTED_AT={world.Instant(TimeSpan.FromHours(5))}\n");
        Assert.False(world.Reader.ReadSignals().RunningFresh);

        world.WriteRunningState("STARTED_AT=now\n");
        Assert.False(world.Reader.ReadSignals().RunningFresh);
    }
}
