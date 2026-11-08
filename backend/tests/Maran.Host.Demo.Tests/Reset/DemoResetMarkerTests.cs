using Maran.Host.Demo.Reset;
using Microsoft.Extensions.Options;

namespace Maran.Host.Demo.Tests.Reset;

/// <summary>The reset marker is one instant in a file, replaced whole and read back exactly.</summary>
public sealed class DemoResetMarkerTests : IDisposable
{
    /// <summary>A directory of this test's own, so no test sees another's marker.</summary>
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "maran-demo-marker-" + Guid.NewGuid().ToString("N"));

    /// <summary>The path the marker is kept at; its directory does not exist yet.</summary>
    private string MarkerPath
    {
        get { return Path.Combine(_directory, "state", "last-reset"); }
    }

    /// <summary>Removes the test's directory.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>With no file there is no reset to report.</summary>
    [Fact]
    public async Task A_missing_marker_reads_as_no_reset()
    {
        Assert.Null(await Marker().ReadAsync(CancellationToken.None));
    }

    /// <summary>An instant written is the instant read, in UTC, with the missing directory created.</summary>
    [Fact]
    public async Task A_written_instant_reads_back_exactly()
    {
        var at = new DateTimeOffset(2026, 11, 7, 12, 30, 15, TimeSpan.FromHours(4)).AddTicks(1234);

        await Marker().WriteAsync(at, CancellationToken.None);
        var read = await Marker().ReadAsync(CancellationToken.None);

        Assert.Equal(at, read);
        Assert.Equal(TimeSpan.Zero, read!.Value.Offset);
    }

    /// <summary>A second write replaces the first and leaves no staging file behind.</summary>
    [Fact]
    public async Task A_second_write_replaces_the_first_and_leaves_nothing_behind()
    {
        var first = new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);
        var second = first.AddHours(2);

        await Marker().WriteAsync(first, CancellationToken.None);
        await Marker().WriteAsync(second, CancellationToken.None);

        Assert.Equal(second, await Marker().ReadAsync(CancellationToken.None));
        Assert.Equal(new[] { MarkerPath }, Directory.GetFileSystemEntries(Path.GetDirectoryName(MarkerPath)!));
    }

    /// <summary>A file that is not an instant is read as no reset, never as a guess.</summary>
    [Fact]
    public async Task A_file_that_is_not_an_instant_reads_as_no_reset()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath)!);
        await File.WriteAllTextAsync(MarkerPath, "not a time\n");

        Assert.Null(await Marker().ReadAsync(CancellationToken.None));
    }

    /// <summary>Builds a marker over this test's path.</summary>
    /// <returns>The marker.</returns>
    private DemoResetMarker Marker()
    {
        return new DemoResetMarker(
            new OptionsWrapper<DemoResetOptions>(new DemoResetOptions { MarkerPath = MarkerPath }));
    }
}
