namespace Maran.Modules.Files.Domain.Enums;

/// <summary>What a listed entry is, as the file manager shows it.</summary>
/// <remarks>
/// A symbolic link is shown as what the agent says it is and carries its own flag beside the kind
/// (<c>IsSymlink</c>), so the kind answers "can this be opened" and the flag answers "is this a link"
/// — two questions, never one field.
/// </remarks>
public enum FileEntryKind
{
    /// <summary>A regular file.</summary>
    File,

    /// <summary>A folder.</summary>
    Directory,

    /// <summary>Anything the manager can neither open nor edit: a socket, a device node.</summary>
    Other,
}
