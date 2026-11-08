using Maran.Modules.Files.Common;
using Maran.Modules.Files.Domain.Enums;
using Maran.Modules.Files.Domain.Policies;
using AgentTrashedEntry = Maran.Agent.Client.Services.FilesService.TrashedEntryDto;

namespace Maran.Modules.Files.Mappers;

/// <summary>Restates one entry of the agent's trash listing as the row the file manager draws.</summary>
public static class TrashEntryMapper
{
    /// <summary>Restates the entry.</summary>
    /// <param name="entry">The agent's entry.</param>
    /// <param name="retentionDays">How many days an entry stays in the trash, which fixes when it expires.</param>
    /// <param name="kindName">The entry kind's name in the request's culture.</param>
    /// <returns>The row, with the instant the retention job will remove the entry.</returns>
    /// <remarks>
    /// A trashed entry is a file or a folder: the agent lists nothing else, so a link or a device never
    /// reaches this mapper as a third kind.
    /// </remarks>
    public static TrashEntryDto From(AgentTrashedEntry entry, int retentionDays, string kindName)
    {
        return new TrashEntryDto(
            entry.TrashId,
            FilesPathSegmentsPolicy.NameOf(entry.OriginalPath),
            entry.OriginalPath,
            entry.IsDirectory ? FileEntryKind.Directory : FileEntryKind.File,
            kindName,
            entry.SizeBytes,
            entry.TrashedAt,
            entry.TrashedAt.AddDays(retentionDays));
    }
}
