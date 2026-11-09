using Maran.Modules.Files.Common;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Files.Queries.ListFileEntries;

/// <summary>The sort whitelist of a folder listing and of a name search.</summary>
/// <remarks>
/// A listing is ordered by the agent, which pages the folder itself, so for it this map only decides
/// which public names are accepted and what the default is (<see cref="SortMap{T}.Resolve"/>). A search
/// is ordered here, in memory, by the same map. The path breaks ties because a search spans folders
/// and two matches can share a name.
/// </remarks>
public static class ListFileEntriesSorts
{
    /// <summary>By name, ascending, by default; the full path breaks ties so a page boundary never repeats a row.</summary>
    public static readonly SortMap<FileEntryDto> Map = new SortMapBuilder<FileEntryDto>()
        .By(FileEntrySortFields.Name, entry => entry.Name)
        .By(FileEntrySortFields.Modified, entry => entry.ModifiedAt)
        .By(FileEntrySortFields.Size, entry => entry.SizeBytes)
        .DefaultTo(FileEntrySortFields.Name, SortDirection.Ascending)
        .TieBreaker(entry => entry.Path);
}
