using System.Globalization;
using Microsoft.Extensions.Options;

namespace Maran.Host.Demo.Reset;

/// <summary>
/// Reads and writes the file that records when the demonstration last reset. The file holds one
/// ISO 8601 instant in UTC and nothing else, so the reset script and an operator can read it with
/// <c>cat</c>.
/// </summary>
public sealed class DemoResetMarker
{
    /// <summary>The round-trip format the instant is written in.</summary>
    private const string InstantFormat = "o";

    /// <summary>Where the marker lives.</summary>
    private readonly string _path;

    /// <summary>Creates the marker.</summary>
    /// <param name="options">The reset settings.</param>
    public DemoResetMarker(IOptions<DemoResetOptions> options)
    {
        _path = options.Value.MarkerPath;
    }

    /// <summary>Reads the instant of the last reset.</summary>
    /// <param name="cancellationToken">Cancelled when the caller gives up.</param>
    /// <returns>The instant, or null when no reset was recorded or the file is unreadable as an instant.</returns>
    public async Task<DateTimeOffset?> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        var text = (await File.ReadAllTextAsync(_path, cancellationToken)).Trim();

        return DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var instant)
            ? instant.ToUniversalTime()
            : null;
    }

    /// <summary>Records a reset. The file is replaced whole, so a reader never sees half an instant.</summary>
    /// <param name="at">The instant of the reset.</param>
    /// <param name="cancellationToken">Cancelled when the caller gives up.</param>
    /// <returns>A task that completes when the marker is in place.</returns>
    public async Task WriteAsync(DateTimeOffset at, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_path))!;
        Directory.CreateDirectory(directory);

        var staging = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(
            staging,
            at.ToUniversalTime().ToString(InstantFormat, CultureInfo.InvariantCulture) + "\n",
            cancellationToken);
        File.Move(staging, _path, overwrite: true);
    }
}
