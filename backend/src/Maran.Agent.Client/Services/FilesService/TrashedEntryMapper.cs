using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.FilesService;

/// <summary>
/// Reads the agent's trash entries into the panel's own mirror type, beside the client because the
/// generated wire types may not leave this project.
/// </summary>
internal static class TrashedEntryMapper
{
    /// <summary>The earliest Unix second a <see cref="DateTimeOffset"/> holds (0001-01-01T00:00:00Z).</summary>
    private const long MinUnixSeconds = -62_135_596_800;

    /// <summary>The latest Unix second a <see cref="DateTimeOffset"/> holds (9999-12-31T23:59:59Z).</summary>
    private const long MaxUnixSeconds = 253_402_300_799;

    /// <summary>Reads one wire entry.</summary>
    /// <param name="entry">The wire message.</param>
    /// <returns>The panel's mirror of it; a trashing time out of range is clamped, not thrown on.</returns>
    public static TrashedEntryDto ToDto(TrashedEntry entry)
    {
        return new TrashedEntryDto(
            entry.TrashId,
            entry.OriginalPath,
            DateTimeOffset.FromUnixTimeSeconds(Math.Clamp(entry.TrashedAtUnix, MinUnixSeconds, MaxUnixSeconds)),
            entry.IsDirectory,
            entry.SizeBytes);
    }
}
