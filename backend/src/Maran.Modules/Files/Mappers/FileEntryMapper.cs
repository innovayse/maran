using Maran.Modules.Files.Common;
using Maran.Modules.Files.Domain.Enums;
using Maran.Modules.Files.Domain.Policies;
using AgentFileEntry = Maran.Agent.Client.Services.FilesService.FileEntryDto;
using AgentFileEntryKind = Maran.Agent.Client.Services.FilesService.FileEntryKind;

namespace Maran.Modules.Files.Mappers;

/// <summary>
/// Translates what the agent says about one directory entry into the wire shape the SPA reads. It
/// restates and never decides: the entry's kind arrives already named, from the handler.
/// </summary>
public static class FileEntryMapper
{
    /// <summary>The module's kind for the agent's kind of an entry.</summary>
    /// <param name="kind">What the agent reported the entry is.</param>
    /// <returns>
    /// A real directory is a directory; everything else is a file. A symbolic link is a file with its
    /// own flag: the listing never follows a link, so the panel cannot say what it points at.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">A kind this module has no name for.</exception>
    public static FileEntryKind KindOf(AgentFileEntryKind kind)
    {
        return kind switch
        {
            AgentFileEntryKind.File => FileEntryKind.File,
            AgentFileEntryKind.Directory => FileEntryKind.Directory,
            AgentFileEntryKind.Symlink => FileEntryKind.File,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown directory entry kind."),
        };
    }

    /// <summary>Builds the wire row of one entry of a folder.</summary>
    /// <param name="entry">The entry as the agent listed it.</param>
    /// <param name="parentPath">The folder it was listed in, relative to the home; empty for the home.</param>
    /// <param name="kindName">The entry's kind named in the request's culture.</param>
    /// <returns>The wire row.</returns>
    public static FileEntryDto From(AgentFileEntry entry, string parentPath, string kindName)
    {
        return new FileEntryDto(
            entry.Name,
            PathOf(parentPath, entry.Name),
            KindOf(entry.Kind),
            kindName,
            entry.SizeBytes,
            entry.ModifiedAt,
            FilesModePolicy.Format(entry.Mode),
            entry.Kind == AgentFileEntryKind.Symlink);
    }

    /// <summary>Joins a folder and a name into the relative path a later request names the entry by.</summary>
    /// <param name="parentPath">The folder, relative to the home; empty for the home.</param>
    /// <param name="name">The entry's own name.</param>
    /// <returns>The entry's path.</returns>
    public static string PathOf(string parentPath, string name)
    {
        return parentPath.Length == 0 ? name : parentPath + FilesPathPolicy.Separator + name;
    }
}
