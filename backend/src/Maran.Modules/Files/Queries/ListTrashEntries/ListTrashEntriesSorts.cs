using Maran.Modules.Files.Common;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Files.Queries.ListTrashEntries;

/// <summary>The sort whitelist of the trash listing: what a client may order by, the default, and the tie-breaker.</summary>
public static class ListTrashEntriesSorts
{
    /// <summary>The sort map. Newest first by default, because what was just trashed is what a restore usually wants.</summary>
    public static readonly SortMap<TrashEntryDto> Map = new SortMapBuilder<TrashEntryDto>()
        .By(TrashEntrySortFields.Name, entry => entry.Name)
        .By(TrashEntrySortFields.Size, entry => entry.SizeBytes)
        .By(TrashEntrySortFields.DeletedAt, entry => entry.DeletedAt)
        .By(TrashEntrySortFields.ExpiresAt, entry => entry.ExpiresAt)
        .DefaultTo(TrashEntrySortFields.DeletedAt, SortDirection.Descending)
        .TieBreaker(entry => entry.Id);
}
