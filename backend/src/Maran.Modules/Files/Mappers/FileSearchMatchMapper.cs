using Maran.Modules.Files.Common;
using Maran.Modules.Files.Domain.Policies;
using AgentFileSearchMatch = Maran.Agent.Client.Services.FilesService.FileSearchMatchDto;

namespace Maran.Modules.Files.Mappers;

/// <summary>Restates one agent search match as the entry the interface lists.</summary>
public static class FileSearchMatchMapper
{
    /// <summary>Translates a match; the entry carries the full path it was found at.</summary>
    /// <param name="match">The agent's match: the path below the home and the entry found there.</param>
    /// <param name="kindName">The entry's kind named in the request's culture, decided by the caller.</param>
    /// <returns>The entry as the interface lists it.</returns>
    public static FileEntryDto ToDto(AgentFileSearchMatch match, string kindName)
    {
        return FileEntryMapper.From(match.Entry, FilesPathSegmentsPolicy.ParentOf(match.Path), kindName);
    }
}
