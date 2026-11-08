using Maran.Agent.Client.Resources;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Services.FilesService;

/// <summary>
/// Reads the agent's directory entries into the panel's own mirror types. It sits beside the client
/// because the generated wire types may not leave this project.
/// </summary>
/// <remarks>
/// It restates and decides nothing. The two facts it reads once, here, so no caller meets a wire
/// default: the kind is the pair (directory, link) folded into one value, and a modification time the
/// agent could not represent in a <see cref="DateTimeOffset"/> is clamped to the nearest one that can
/// be rather than thrown on, because a listing of a customer's tree must survive a file whose clock
/// is wrong.
/// </remarks>
internal static class FileEntryMapper
{
    /// <summary>The earliest Unix second a <see cref="DateTimeOffset"/> holds (0001-01-01T00:00:00Z).</summary>
    private const long MinUnixSeconds = -62_135_596_800;

    /// <summary>The latest Unix second a <see cref="DateTimeOffset"/> holds (9999-12-31T23:59:59Z).</summary>
    private const long MaxUnixSeconds = 253_402_300_799;

    /// <summary>Reads one wire entry.</summary>
    /// <param name="entry">The wire message.</param>
    /// <returns>The panel's mirror of it.</returns>
    public static FileEntryDto ToDto(DirectoryEntry entry)
    {
        return new FileEntryDto(
            entry.Name,
            KindOf(entry),
            entry.SizeBytes,
            entry.Mode,
            DateTimeOffset.FromUnixTimeSeconds(Math.Clamp(entry.ModifiedAtUnix, MinUnixSeconds, MaxUnixSeconds)),
            entry.Uid,
            entry.Gid,
            entry.LinkCount);
    }

    /// <summary>Reads the success branch of a directory listing.</summary>
    /// <param name="ok">The wire message.</param>
    /// <returns>The page of entries and its counts.</returns>
    public static DirectoryListingDto ToDto(ListDirectoryOk ok)
    {
        return new DirectoryListingDto(
            ok.Entries.Select(ToDto).ToList(),
            ok.TotalEntries,
            ok.SkippedEntries);
    }

    /// <summary>Reads the success branch of a name search.</summary>
    /// <param name="ok">The wire message.</param>
    /// <returns>
    /// The matches and whether a bound stopped the search, or <c>AgentInvalidResponse</c> when a match
    /// carries no entry: a zeroed entry would read as an empty file named nothing.
    /// </returns>
    public static Result<FileSearchResultDto> Read(SearchEntriesOk ok)
    {
        var matches = new List<FileSearchMatchDto>(ok.Matches.Count);
        foreach (var match in ok.Matches)
        {
            if (match.Entry is null)
            {
                return Result<FileSearchResultDto>.Fail(
                    Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure));
            }

            matches.Add(new FileSearchMatchDto(match.Path, ToDto(match.Entry)));
        }

        return Result<FileSearchResultDto>.Ok(new FileSearchResultDto(matches, ok.Truncated, ok.SkippedEntries));
    }

    /// <summary>Writes the panel's sort order as the wire enum.</summary>
    /// <param name="sort">The order asked for.</param>
    /// <returns>The wire value.</returns>
    public static DirectorySort ToWire(DirectorySortOrder sort)
    {
        return sort switch
        {
            DirectorySortOrder.Name => DirectorySort.Name,
            DirectorySortOrder.Size => DirectorySort.Size,
            DirectorySortOrder.Modified => DirectorySort.Modified,
            _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, "Unknown directory sort order."),
        };
    }

    /// <summary>Folds the wire's directory and link flags into one kind; a link wins, as the agent never follows one.</summary>
    /// <param name="entry">The wire entry.</param>
    /// <returns>The entry's kind.</returns>
    private static FileEntryKind KindOf(DirectoryEntry entry)
    {
        if (entry.IsSymlink)
        {
            return FileEntryKind.Symlink;
        }

        return entry.IsDirectory ? FileEntryKind.Directory : FileEntryKind.File;
    }
}
