using System.Globalization;
using Maran.Modules.Files.Common;
using Maran.Modules.Files.Domain.Policies;
using AgentFileEntry = Maran.Agent.Client.Services.FilesService.FileEntryDto;
using AgentFileEntryKind = Maran.Agent.Client.Services.FilesService.FileEntryKind;

namespace Maran.Modules.Files.Mappers;

/// <summary>Restates the agent's description of one entry as the properties the interface shows.</summary>
public static class FileInfoMapper
{
    /// <summary>Translates an entry.</summary>
    /// <param name="entry">The agent's entry.</param>
    /// <param name="path">The entry's path below the home, as the caller named it.</param>
    /// <param name="kindName">The entry's kind named in the request's culture, decided by the caller.</param>
    /// <returns>
    /// The properties. The owner and group are the numeric ids the agent reports, shown read-only. The link
    /// target is absent: the agent's browse contract reports that an entry is a link and never where it points.
    /// </returns>
    public static FileInfoDto From(AgentFileEntry entry, string path, string kindName)
    {
        return new FileInfoDto(
            entry.Name,
            path,
            FileEntryMapper.KindOf(entry.Kind),
            kindName,
            entry.SizeBytes,
            entry.ModifiedAt,
            FilesModePolicy.Format(entry.Mode),
            entry.Kind == AgentFileEntryKind.Symlink,
            entry.OwnerId.ToString(CultureInfo.InvariantCulture),
            entry.GroupId.ToString(CultureInfo.InvariantCulture));
    }
}
